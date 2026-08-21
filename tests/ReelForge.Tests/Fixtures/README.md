# Media test fixtures

`test_video.mp4` is a user-supplied, repository-local fixture used only by network-isolated automated tests.

- SHA-256: `3F2F8F6AF7B559441724BF1F3F9532F9D79017049E174E173679811F30CB9FC8`
- Size: 1,642,405 bytes
- Duration: 10.125 seconds
- Video: H.264, 1344x768, 24 fps
- Audio: AAC-LC stereo, 32 kHz

Tests serve these bytes through custom `HttpMessageHandler` instances. They must never fetch this fixture or any generated output from the public network.
