# Roaming Fish Previews v0.15.0

Finite-bite correctness update for the Animal Crossing-inspired fishing
overhaul for STORY OF SEASONS: Grand Bazaar.

Highlights:

- Finite, location-accurate schools derived from connected fishing-water cells
- Native eligibility lottery and synchronized visible-fish catches
- Bobber pursuit with visible test nibbles before the final bite
- No phantom catch animation when no visible fish owns the bobber
- Slow per-pool population recovery after catches
- Hook reactions and running-footstep shoreline scatter
- Natural head-anchored spine flex driven by swimming speed and turning
- Clean BepInEx ZIP installation and complete source/reverse-engineering notes

The native rod's caller continued its bite animation even when `DecideFish`
was suppressed. Version 0.15.0 gates the caller's bite timer instead, releasing
it only when the visible fish finishes its final nibble. Empty casts remain
waiting and can still be canceled or reeled in normally.

Extract the ZIP into the Grand Bazaar game directory and merge its `BepInEx`
folder. A current BepInEx 6 Unity IL2CPP x64 build is required.

Extracted game geometry is intentionally excluded. The repository includes an
optional local extraction tool for owners who want flex enabled on the one
native shadow mesh whose Unity Read/Write flag is disabled.
