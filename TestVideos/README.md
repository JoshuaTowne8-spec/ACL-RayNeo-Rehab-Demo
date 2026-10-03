# Real-device demonstration videos

These videos document the current ACL rehabilitation prototype running with the
RayNeo X3 Pro and the ESP32 sensor workflow.

## TestVideo01 — Outdoor first-person AR demonstration

[Open TestVideo01.mp4](TestVideo01.mp4)

This video shows the outdoor visual effect from the user's point of view. It is
intended to demonstrate route placement, visibility, scale, and the appearance
of the echo targets in a real test area.

## TestVideo02 — Successful echo collection

[Open TestVideo02.mp4](TestVideo02.mp4)

This video shows a successful collection event. The echo is collected when the
user reaches it and performs the intended cushioned running-landing action while
the corresponding ESP32 sensor conditions are satisfied.

Current sensor rules:

- Right-side echo: `Pressure 1 > 60` and right knee angle `> 10 degrees`.
- Left-side echo: `Pressure 2 > 60`.
- Collection distance: no more than `0.3 m`.

## TestVideo03 — Outdoor third-person demonstration

[Open TestVideo03.mp4](TestVideo03.mp4)

This video presents the outdoor test from a third-person viewpoint, showing the
participant, physical test area, headset use, and the overall running workflow.

## Repository storage

All MP4 files in this directory are tracked with Git LFS. Run `git lfs install`
before cloning or pulling the repository to obtain the full video files.

