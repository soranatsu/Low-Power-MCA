#include "ad7980.h"
#include "main.h"

/*
 * Hardware mode fixed by the analog board:
 *   - SDI is tied to VIO, selecting AD7980 three-wire CS mode.
 *   - CNV is generated only by the external trigger/monostable chain.
 *   - CNV returns low before tCONV(min), enabling the SDO busy indicator.
 *   - SDO has a 47 kohm pull-up.  Its high-Z -> low transition at end of
 *     conversion reaches PA0 (EXTI) and PA6 (data) on the same net.
 *
 * The complete 17-clock transaction is normally executed in EXTI0.
 * EXTI0 priority 5 is higher than USB priority 6, so USB cannot stretch the
 * bit-banged clock or the PH_RESTART pulse.  The ISR takes roughly 3 us at
 * 150 MHz, safely below the 10 us event spacing required at 100 kHz.  Samples
 * are handed to the main loop through a 1023-entry SPSC ring buffer; USB
 * formatting therefore cannot delay the ADC readout.
 *
 * A missed falling edge would otherwise deadlock three-wire busy mode: SDO
 * remains low until it receives the 17 clocks, so there is no second edge to
 * wake the MCU. AD7980_Service() therefore performs a guarded low-level poll.
 * A real Busy falling-edge transaction is always retained.  The PA0 level
 * observed after readout is only diagnostic because the shared SDO/BUSY node
 * can still appear low when sampled by firmware. After a confirmed high, the
 * first guarded low is retained as a missed conversion. A persistent low is
 * flushed with a complete 17-clock transaction but is not added to the
 * spectrum. Every full transaction ends with PH_RESTART.
 */

#define AD7980_SAMPLE_QUEUE_SIZE 1024U
#define AD7980_SAMPLE_QUEUE_MASK (AD7980_SAMPLE_QUEUE_SIZE - 1U)
#define PH_RESTART_PULSE_US      1U
#define RECOVERY_GUARD_US        6U
#define STUCK_RETRY_US           1000U

static AD7980_Sample sample_queue[AD7980_SAMPLE_QUEUE_SIZE];
static volatile uint32_t queue_head;
static volatile uint32_t queue_tail;
static volatile uint32_t busy_count;
static volatile uint32_t recovery_count;
static volatile uint32_t post_read_low_count;
static volatile uint32_t overrun_count;
static volatile uint32_t sequence;
static volatile uint32_t last_capture_cycles;
static volatile uint32_t next_recovery_cycles;
static volatile uint8_t poll_capture_armed;

static inline void adc_timing_delay(void)
{
  /* Four 150 MHz NOPs are 26.7 ns, exceeding tDSDO(max)=11 ns and
   * tDIS(max)=20 ns for VIO=3.3 V. GPIO stores add further margin. */
  __NOP();
  __NOP();
  __NOP();
  __NOP();
}

static void delay_cycles(uint32_t cycles)
{
  const uint32_t start = DWT->CYCCNT;
  while ((uint32_t)(DWT->CYCCNT - start) < cycles)
  {
  }
}

void AD7980_ResetStatistics(void)
{
  const uint32_t primask = __get_PRIMASK();
  __disable_irq();
  queue_head = 0U;
  queue_tail = 0U;
  busy_count = 0U;
  recovery_count = 0U;
  post_read_low_count = 0U;
  overrun_count = 0U;
  sequence = 0U;
  last_capture_cycles = DWT->CYCCNT;
  next_recovery_cycles = DWT->CYCCNT;
  /* A statistics clear can occur while SDO is already low.  Arm one guarded
   * full recovery so an already-completed conversion cannot wait forever for
   * another falling edge. */
  poll_capture_armed = 1U;
  __HAL_GPIO_EXTI_CLEAR_IT(ADC_BUSY_IRQ_Pin);
  __set_PRIMASK(primask);
}

void AD7980_Init(void)
{
  HAL_NVIC_DisableIRQ(ADC_BUSY_IRQ_EXTI_IRQn);
  HAL_GPIO_WritePin(ADC_SCK_GPIO_Port, ADC_SCK_Pin, GPIO_PIN_RESET);
  HAL_GPIO_WritePin(PH_RESTART_GPIO_Port, PH_RESTART_Pin, GPIO_PIN_RESET);

  CoreDebug->DEMCR |= CoreDebug_DEMCR_TRCENA_Msk;
  DWT->CYCCNT = 0U;
  DWT->CTRL |= DWT_CTRL_CYCCNTENA_Msk;

  AD7980_ResetStatistics();
  __HAL_GPIO_EXTI_CLEAR_IT(ADC_BUSY_IRQ_Pin);
  HAL_NVIC_EnableIRQ(ADC_BUSY_IRQ_EXTI_IRQn);
}

static uint8_t ad7980_capture(uint8_t store_sample)
{
  AD7980_Sample captured;
  uint16_t value = 0U;
  const uint32_t event_sequence = sequence + 1U;
  uint8_t released;

  captured.timestamp_ms = HAL_GetTick();
  captured.sequence = event_sequence;

  /* SCK starts and ends low. Each falling edge clocks out the next bit.
   * Sampling after adc_timing_delay() satisfies tDSDO(max). */
  for (uint32_t bit = 0U; bit < 16U; ++bit)
  {
    ADC_SCK_GPIO_Port->BSRR = ADC_SCK_Pin;
    adc_timing_delay();
    ADC_SCK_GPIO_Port->BRR = ADC_SCK_Pin;
    adc_timing_delay();
    value = (uint16_t)((value << 1U) |
      (((ADC_SDO_GPIO_Port->IDR & ADC_SDO_Pin) != 0U) ? 1U : 0U));
  }

  /* Optional 17th falling edge: no bit is sampled. It releases SDO to high-Z.
   * The following delay is longer than tDIS(max)=20 ns. */
  ADC_SCK_GPIO_Port->BSRR = ADC_SCK_Pin;
  adc_timing_delay();
  ADC_SCK_GPIO_Port->BRR = ADC_SCK_Pin;
  adc_timing_delay();

  /* ADG721 IN1 high closes S1-D1 to ground and discharges the hold capacitor.
   * Restart is asserted only after D0 was sampled and SDO was released. */
  PH_RESTART_GPIO_Port->BSRR = PH_RESTART_Pin;
  delay_cycles((SystemCoreClock / 1000000U) * PH_RESTART_PULSE_US);
  PH_RESTART_GPIO_Port->BRR = PH_RESTART_Pin;
  last_capture_cycles = DWT->CYCCNT;

  /* PA0 is checked after the transaction only as a diagnostic.  On the tested
   * ARM/BRM boards this shared SDO/BUSY node can still read low here even though
   * the 16-bit word is valid and the next external CNV rearms the interface.
   * Rejecting such a real Busy-edge transaction caused the v1.5.1 half-rate
   * symptom. store_sample distinguishes a real conversion from a later
   * non-counting full flush and therefore prevents duplicate spectrum data. */
  released = ((ADC_BUSY_IRQ_GPIO_Port->IDR & ADC_BUSY_IRQ_Pin) != 0U) ? 1U : 0U;
  if (released == 0U)
  {
    post_read_low_count++;
  }

  if (store_sample != 0U)
  {
    captured.raw = value;
    busy_count++;
    sequence = event_sequence;
    {
      const uint32_t next = (queue_head + 1U) & AD7980_SAMPLE_QUEUE_MASK;
      if (next == queue_tail)
      {
        overrun_count++;
      }
      else
      {
        sample_queue[queue_head] = captured;
        queue_head = next;
      }
    }
  }

  /* SDO data transitions share PA0, so discard EXTI events caused by readout. */
  __HAL_GPIO_EXTI_CLEAR_IT(ADC_BUSY_IRQ_Pin);
  return released;
}

void AD7980_BusyISR(void)
{
  /* Ignore a stale/spurious EXTI pending bit after SDO has already released. */
  if ((ADC_BUSY_IRQ_GPIO_Port->IDR & ADC_BUSY_IRQ_Pin) == 0U)
  {
    (void)ad7980_capture(1U);
    /* Do not treat the immediate PA0 level as a permanent state.  The 47 kohm
     * external pull-up and line capacitance can keep the released SDO node low
     * for much longer than tDIS.  A real high observation rearms polling. */
    poll_capture_armed = 0U;
    next_recovery_cycles = DWT->CYCCNT +
      (SystemCoreClock / 1000000U) * RECOVERY_GUARD_US;
  }
}

void AD7980_Service(void)
{
  const uint32_t recovery_guard_cycles =
    (SystemCoreClock / 1000000U) * RECOVERY_GUARD_US;
  uint32_t primask;
  uint32_t now;

  if ((ADC_BUSY_IRQ_GPIO_Port->IDR & ADC_BUSY_IRQ_Pin) != 0U)
  {
    poll_capture_armed = 1U;
    return;
  }

  now = DWT->CYCCNT;
  if (((uint32_t)(now - last_capture_cycles) < recovery_guard_cycles) ||
      ((int32_t)(now - next_recovery_cycles) < 0))
  {
    return;
  }

  /* Main-context recovery must have the same timing isolation as EXTI0. */
  primask = __get_PRIMASK();
  __disable_irq();
  if ((ADC_BUSY_IRQ_GPIO_Port->IDR & ADC_BUSY_IRQ_Pin) == 0U)
  {
    __HAL_GPIO_EXTI_CLEAR_IT(ADC_BUSY_IRQ_Pin);
    recovery_count++;
    /* First low level after a confirmed high is a missed conversion and is
     * retained.  If the line never returned high, perform a complete 17-clock
     * flush without adding a duplicate spectrum entry.  Both paths pulse
     * PH_RESTART, so an abnormal low level can no longer suppress peak-hold
     * release indefinitely. */
    const uint8_t store_sample = poll_capture_armed;
    const uint8_t released = ad7980_capture(store_sample);
    poll_capture_armed = 0U;
    if (released == 0U)
    {
      next_recovery_cycles = DWT->CYCCNT +
        (SystemCoreClock / 1000000U) * STUCK_RETRY_US;
    }
    else
    {
      poll_capture_armed = 1U;
      next_recovery_cycles = DWT->CYCCNT;
    }
  }
  __set_PRIMASK(primask);
}

bool AD7980_TryRead(AD7980_Sample *sample)
{
  uint32_t primask;

  if (sample == NULL) return false;
  primask = __get_PRIMASK();
  __disable_irq();
  if (queue_tail == queue_head)
  {
    __set_PRIMASK(primask);
    return false;
  }
  *sample = sample_queue[queue_tail];
  queue_tail = (queue_tail + 1U) & AD7980_SAMPLE_QUEUE_MASK;
  __set_PRIMASK(primask);
  return true;
}

bool AD7980_IsSdoLow(void)
{
  return (ADC_BUSY_IRQ_GPIO_Port->IDR & ADC_BUSY_IRQ_Pin) == 0U;
}

uint32_t AD7980_GetBusyCount(void)
{
  return busy_count;
}

uint32_t AD7980_GetRecoveryCount(void)
{
  return recovery_count;
}

uint32_t AD7980_GetPostReadLowCount(void)
{
  return post_read_low_count;
}

uint32_t AD7980_GetOverrunCount(void)
{
  return overrun_count;
}

uint32_t AD7980_GetQueueDepth(void)
{
  return (queue_head - queue_tail) & AD7980_SAMPLE_QUEUE_MASK;
}
