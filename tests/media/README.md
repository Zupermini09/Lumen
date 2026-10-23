`smoke-6s.mp4` is a generated test pattern (ffmpeg `testsrc2` + 440 Hz tone, H.264/AAC, 6 s) used by the CI smoke tests:

```
ffmpeg -f lavfi -i testsrc2=size=320x180:rate=24 -f lavfi -i sine=frequency=440:sample_rate=44100 -t 6 \
  -c:v libx264 -preset veryslow -crf 36 -pix_fmt yuv420p -c:a aac -b:a 32k -movflags +faststart smoke-6s.mp4
```
