# Media smoke and baseline font candidates

These exact OFL-1.1 fonts support the repeatable title/caption smoke test and the proposed product fallback plan. They are not by themselves a shipping-font decision or public-distribution approval.

- Noto Sans Regular v2.015: Latin, punctuation, and diacritics;
- Noto Sans Arabic Regular v2.013: Arabic fallback and shaping evidence;
- Noto Sans CJK SC Regular Sans2.004: Simplified Chinese fallback for the approved fixture locale.

Their SHA-256 identities are recorded in `../baseline-profile.json`; each binary has a corresponding license text under `licenses/`. The runtime validator checks those exact bytes. The smoke suite uses explicit font paths and prohibits system-font fallback from defining the result. Color emoji is outside the baseline.
