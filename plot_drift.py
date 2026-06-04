#!/usr/bin/env python3
"""Plot NTP drift over time from a CSV file.

The CSV is expected to have a LocalTime column (ISO-8601 timestamps) and a
Drift column (a duration string like "-00:00:00.0032239" = HH:MM:SS.fffffff).
Any other columns (e.g. NtpTime, MarginOfError) are ignored.

Usage:
    python3 plot_drift.py ntp-drift.csv
    python3 plot_drift.py ntp-drift.csv -o out.png --x-col LocalTime --y-col Drift
"""
import argparse
import csv
import re
import sys
from datetime import datetime, timezone


def parse_timestamp(value):
    """Parse an ISO-8601 timestamp, tolerating 7-digit fractional seconds and a 'Z' suffix."""
    text = value.strip()
    # Python's fromisoformat (< 3.11) accepts at most 6 fractional digits and no 'Z'.
    text = text.replace("Z", "+00:00")
    # Trim fractional seconds to 6 digits.
    text = re.sub(r"(\.\d{6})\d+", r"\1", text)
    return datetime.fromisoformat(text)


def parse_duration_seconds(value):
    """Parse a "[-]HH:MM:SS.fffffff" duration string into a float number of seconds."""
    text = value.strip()
    sign = 1.0
    if text.startswith("-"):
        sign = -1.0
        text = text[1:]
    elif text.startswith("+"):
        text = text[1:]
    hours, minutes, seconds = text.split(":")
    total = int(hours) * 3600 + int(minutes) * 60 + float(seconds)
    return sign * total


def load(path, x_col, y_col):
    xs, ys = [], []
    with open(path, newline="") as f:
        reader = csv.DictReader(f)
        if x_col not in reader.fieldnames or y_col not in reader.fieldnames:
            sys.exit(
                f"error: columns {x_col!r}/{y_col!r} not found. "
                f"Available: {reader.fieldnames}"
            )
        for row in reader:
            xs.append(parse_timestamp(row[x_col]))
            ys.append(parse_duration_seconds(row[y_col]))
    return xs, ys


def main():
    ap = argparse.ArgumentParser(description="Plot NTP drift over time to a PNG.")
    ap.add_argument("csv_file", help="Input CSV file")
    ap.add_argument("-o", "--output", help="Output PNG path (default: <input>.png)")
    ap.add_argument("--x-col", default="LocalTime", help="X-axis column (default: LocalTime)")
    ap.add_argument("--y-col", default="Drift", help="Y-axis column (default: Drift)")
    ap.add_argument("--unit", choices=["s", "ms", "us"], default="ms",
                    help="Y-axis unit for drift (default: ms)")
    args = ap.parse_args()

    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    import matplotlib.dates as mdates

    xs, ys = load(args.csv_file, args.x_col, args.y_col)
    if not xs:
        sys.exit("error: no data rows found")

    scale = {"s": 1.0, "ms": 1e3, "us": 1e6}[args.unit]
    ys = [y * scale for y in ys]

    output = args.output or (args.csv_file.rsplit(".", 1)[0] + ".png")

    fig, ax = plt.subplots(figsize=(12, 6))
    ax.plot(xs, ys, linewidth=1, color="tab:blue")
    ax.axhline(0, color="grey", linewidth=0.8, linestyle="--")
    ax.set_xlabel(args.x_col)
    ax.set_ylabel(f"{args.y_col} ({args.unit})")
    ax.set_title(f"{args.y_col} over {args.x_col}")
    ax.grid(True, alpha=0.3)
    ax.xaxis.set_major_formatter(mdates.DateFormatter("%H:%M:%S"))
    fig.autofmt_xdate()
    fig.tight_layout()
    fig.savefig(output, dpi=150)
    print(f"wrote {output} ({len(xs)} points)")


if __name__ == "__main__":
    main()
