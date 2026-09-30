"""Offline rational examples for THG-PRICE-001; no OsEngine/vendor execution.

Run from any directory: python path/to/check_signed_price_cases.py
Literal expected values live in the adjacent JSON. This checks the proposed
mathematics, not .NET decimal overflow, native parsers, callbacks or trading.
"""

import json
from fractions import Fraction as F
from pathlib import Path


def floor(value):
    return value.numerator // value.denominator


def ceil(value):
    return -floor(-value)


def price(value):
    result = F(value)
    if (result * 100000).denominator != 1:
        raise ValueError("price_precision")
    return result


def tick(value):
    result = F(value)
    if result <= 0 or (result * 100000).denominator != 1:
        raise ValueError("tick_domain")
    return result


def quantize(value, step, direction):
    return step * (floor(value / step) if direction == "down" else ceil(value / step))


def evaluate(case):
    op = case["op"]
    if op == "grid":
        low, high, step, count = price(case["low"]), price(case["high"]), tick(case["tick"]), case["n"]
        if low >= high or count < 2:
            raise ValueError("range_or_count")
        if (low / step).denominator != 1 or (high / step).denominator != 1:
            raise ValueError("off_tick")
        first, last = int(low / step), int(high / step)
        span = last - first
        if span < count - 1:
            raise ValueError("insufficient_ticks")
        result = [step * (first + j * span // (count - 1)) for j in range(count)]
        assert result[0] == low and result[-1] == high
        assert len(set(result)) == count
        assert all(low <= p <= high and (p / step).denominator == 1 for p in result)
        return result
    if op == "round":
        return quantize(F(case["value"]), tick(case["tick"]), case["direction"])
    if op in ("budget", "fixed"):
        money, collateral, step, count = F(case["money"]), F(case["collateral"]), F(case["volume_step"]), case["n"]
        if money <= 0 or collateral <= 0 or step <= 0 or count < 2:
            raise ValueError("budget_domain")
        if op == "fixed":
            fixed = F(case["fixed"])
            if fixed <= 0 or (fixed / step).denominator != 1:
                raise ValueError("volume_domain")
            quantities = [fixed] * count
        else:
            totals = [floor(F(i + 1) * money / (count * collateral * step)) for i in range(count)]
            quantities = [step * (totals[i] - (totals[i - 1] if i else 0)) for i in range(count)]
        if any(q < F(case.get("minimum_volume", case["volume_step"])) for q in quantities):
            raise ValueError("minimum_volume")
        reserve = sum(quantities) * collateral
        if reserve > money:
            raise ValueError("insufficient_budget")
        return quantities + [reserve, money - reserve]
    if op == "average":
        fills = [(F(p), F(q)) for p, q in case["fills"]]
        if any(q <= 0 for p, q in fills) or not fills:
            raise ValueError("no_positive_fills")
        return sum(p * q for p, q in fills) / sum(q for p, q in fills)
    if op == "tp":
        basis, markup, step = F(case["basis"]), F(case["markup"]), tick(case["tick"])
        if markup <= 0:
            raise ValueError("markup_domain")
        long_side = case["side"] == "long"
        return quantize(basis + (markup if long_side else -markup), step, "up" if long_side else "down")
    if op == "pnl":
        difference = F(case["exit"]) - F(case["entry"])
        return difference * (1 if case["side"] == "long" else -1) * F(case["quantity"]) * F(case["kappa"]) - F(case["fees"])
    if op == "percent_distance":
        basis, percent = F(case["base"]), F(case["percent"])
        if basis <= 0 or percent < 0:
            raise ValueError("percent_domain")
        return basis * percent / 100
    raise AssertionError("Unknown operation: " + op)


def main():
    payload = json.loads(Path(__file__).with_name("signed-price-cases.json").read_text(encoding="utf-8"))
    ids = set()
    for case in payload["cases"]:
        assert case["id"] not in ids
        ids.add(case["id"])
        try:
            actual = evaluate(case)
        except ValueError as error:
            assert str(error) == case.get("error"), (case["id"], str(error))
        else:
            assert "error" not in case, (case["id"], "Expected rejection")
            expected = case["expected"]
            expected = [F(v) for v in expected] if isinstance(expected, list) else F(expected)
            assert actual == expected, (case["id"], actual, expected)
    print(f"PASS {len(ids)}/{len(ids)} proposed numerical examples; native/runtime NOT_RUN")


if __name__ == "__main__":
    main()
