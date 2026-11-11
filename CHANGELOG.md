# Changelog

## 0.15.1

- Fish turn toward the bobber before advancing and swim along their facing direction
- Approach slows near the bobber; nibbles start from the aligned position without a backward jump
- Fixed reel input after a valid bite by allowing the native timer to run after the final nibble

## 0.15.0

- Fixed off-target casts entering a phantom catch animation with no visible bite
- Parked the rod's native bite timer until a finite fish completes its final nibble
- Preserved the valid internal lottery row and native cancel/reel-in lifecycle

## 0.14.0

- Added finite, connected-pool fish populations with staggered recovery
- Added location- and condition-accurate native lottery integration
- Added visible bobber pursuit, test nibbles, and synchronized catches
- Added hook reactions and running-footstep shoreline scatter
- Added independent native shadow prefabs with normalized water placement
- Added head-anchored, speed- and turn-responsive curved-spine deformation
- Added optional local writable-mesh extraction for all five real-fish classes
