"""Record STM32 USB CDC text/CSV to a file. Requires: pip install pyserial."""

import argparse
from datetime import datetime
import serial


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("port", help="Windows COM port, e.g. COM7")
    parser.add_argument("output", help="Output .csv or .txt path")
    parser.add_argument("--baud", type=int, default=115200,
                        help="CDC line-coding value; USB throughput is unchanged")
    args = parser.parse_args()

    with serial.Serial(args.port, args.baud, timeout=1) as device, open(
        args.output, "a", encoding="utf-8", newline=""
    ) as output:
        output.write(f"# capture_started={datetime.now().isoformat()}\n")
        print(f"Recording {args.port} -> {args.output}; Ctrl+C to stop")
        try:
            while True:
                data = device.readline()
                if data:
                    text = data.decode("utf-8", errors="replace")
                    output.write(text)
                    output.flush()
                    print(text, end="")
        except KeyboardInterrupt:
            output.write(f"# capture_stopped={datetime.now().isoformat()}\n")


if __name__ == "__main__":
    main()
