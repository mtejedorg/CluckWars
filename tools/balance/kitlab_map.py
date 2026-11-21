"""The Kit Lab <-> delta field mapping, shared by kitlab_to_proposal.py and delta_to_kitlab.py.

Mirrors the Kit Lab page's `buildDelta` (BASEKEY, DP_PASSIVE and r4 in the page source, read 2026-11-17). Keep in sync with it.
"""

# class base stat key in the `classstats` documents -> ChickenStatsSO field
BASEKEY = {"move": "MoveSpeed", "carry": "CargoCapacity", "peckAmt": "PeckAmount", "peckCd": "PeckCooldown",
           "peckCast": "PeckCastSeconds", "turn": "TurnSpeed", "size": "Scale"}

# passive stat key in the `kits` documents (a percentage) -> (PassiveAbilitySO field, percentage -> field value,
# field value -> percentage)
PASSIVE = {
    "cdr": ("CooldownMultiplier", lambda v: 1 - v / 100, lambda f: (1 - f) * 100),
    "steal": ("StealMultiplier", lambda v: 1 + v / 100, lambda f: (f - 1) * 100),
    "cc": ("DurationMultiplier", lambda v: v / 100, lambda f: f * 100),
    "slowIn": ("Resist", lambda v: v / 100, lambda f: f * 100),
    "kb": ("KnockbackMultiplier", lambda v: v / 100, lambda f: f * 100),
    "bank": ("DepositRateMultiplier", lambda v: v / 100, lambda f: f * 100),
    "ccOut": ("OutgoingDurationMultiplier", lambda v: v / 100, lambda f: f * 100),
}


def r4(v):
    """The page's r4: round to 4 decimals (JavaScript Math.round, halves go up)."""
    import math
    return math.floor(v * 10000 + 0.5) / 10000


def is_num(v):
    import math
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)
