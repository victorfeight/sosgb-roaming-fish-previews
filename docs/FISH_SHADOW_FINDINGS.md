# Grand Bazaar fish-preview reverse engineering

## Corrected native lifecycle

Grand Bazaar is a Unity IL2CPP title. `BokuMono.FishShadow` is misleadingly
named: it is not a persistent, pre-cast water population. The game loads it
after a cast, places it at the buoy, and leaves it there during that fishing
attempt. On a successful finish, native `FishShadow.SetTarget` and
`FishShadow.FixedUpdate` turn the same object into a short ballistic catch
presentation, after which it is deactivated and released. The game adds this
controller dynamically after loading the selected model.

That explains the failed 0.1-0.3 prototypes. They cloned or retained an object
owned by the active-cast lifecycle. Logs could truthfully report an active
renderer while the native owner was deactivating or releasing the underlying
object and assets. `Readable Fish Shadows 0.1.2` scales this post-cast object;
that scale carries into its catch flight. It still does not create any
pre-cast water population.

The inspected build's native call chain is concrete:

| Native address | Method / observed responsibility |
|---|---|
| `0x18076F8C0` | `FishManager.<LoadShadowModel>b__0`: attach `FishShadow`, activate it, place it at the buoy |
| `0x180766573` | `FishManager.FinishFishing`: call `FishShadow.SetTarget` |
| `0x18076CC80` | `FishShadow.FixedUpdate`: advance the flight and release at completion |
| `0x180765420` | `FishManager.CleanShadowObject`: release the cast-owned shadow object |

## Raw silhouette assets

The Addressables catalog maps all eleven `FishingMasterData.ShadowModelId`
values to real prefab bundles. `FishManager.LoadShadowModel` calls
`ResourceManager.InstantiatePrefab(modelId, ...)` and then adds `FishShadow`.
The raw prefab can be instantiated independently without attaching `FishShadow`.
It is an upright caught-fish model rather than an authored flat water-shadow
asset, so 0.14.0 loads it independently, disables its cast-flight controller,
rotates its long Y axis onto the water plane, normalizes it from the measured
mesh dimensions, and gives its renderer a dark silhouette tint. Version 0.7.0's
flat body/tail/fin mesh remains only as the asynchronous-load fallback.

The prefab meshes are authored upright and very small at scale 1. For example:

| Model | Native dimensions (m) | Authored long axis |
|---|---:|---|
| `anml_wld_010_00` | 0.063 x 0.325 x 0.063 | Y |
| `anml_wld_010_01` | 0.352 x 0.815 x 0.476 | Y |
| `anml_wld_010_02` | 0.063 x 0.325 x 0.063 | Y |
| `anml_wld_020_00` | 0.591 x 1.112 x 0.552 | Y |
| `anml_wld_030_00` | 0.539 x 0.911 x 0.405 | Y |

### Is there a species-specific catch model?

No species-specific 3D fish model is referenced by the game's item data. Every
actual fish row from item `114000` through `114103` uses `ModelId = FieldItem`,
the same generic field-pickup prefab. Each row does have a species-specific
`IconId` such as `item_icon_114001`. The native fishing path instead carries
only `FishData.ShadowModelId`, loads one of the five shared fish-class meshes,
and flies that same mesh toward the player after `FinishFishing` calls
`FishShadow.SetTarget`.

Therefore a species-accurate *3D* reel-up cannot be extracted from existing game
assets: those models are not present. A species-accurate *2D artwork* reveal is
feasible by loading the caught item's icon into the generic `FieldItem`
billboard or a dedicated sprite renderer during the successful flight. That
would be a new presentation choice, not restoration of an unused vanilla fish
model.

The 0.5 experiment cloned a cast-owned instance, so the native fishing lifecycle
could deactivate or release it. Version 0.14.0 instead asks `ResourceManager`
for an independent prefab instance under each roaming root. Model ID and average
catch size still determine readable length, while the exact native mesh supplies
the outline. The first procedural fallback and first completed native load are
logged separately, so placement, loading, and rendering can be distinguished.

### Swimming deformation evidence

The local ACNL decomp identifies `AcFishFieldBase` as inheriting
`ResourceGetSkeletal`. That is concrete evidence that Animal Crossing field
fish use skeletal resources. Its fish animation functions are not yet
decompiled, so the exact bone layout and speed curves cannot be claimed; the
most defensible interpretation is a looping authored skeletal swim animation
combined with actor steering, not a fluid simulation.

Grand Bazaar's raw shadow bundles use a different structure: `fish_body` is a
plain `MeshFilter`/`MeshRenderer`, not a `SkinnedMeshRenderer`; the prefab has
an `Animator`, but its controller reference is null and there are no fish-body
bones. The five classes referenced by actual fish were extracted locally from
the installed Addressables bundles into the compact `RFM1` format. Bounds,
vertex/index validity, and bundle/payload hashes are recorded in the generated
manifest. Version 0.14.0 reconstructs a private writable mesh for each visible
actor, places it into the independently loaded native prefab, and retains the
native renderer materials. Every other frame, a head-anchored centerline wave
grows toward the tail, rotates each mesh cross-section with the curved spine,
updates its normals, and adds speed- and turn-responsive motion. This applies to
all five classes, including `anml_wld_010_01`, whose shipped Unity mesh has
Read/Write disabled. The five payloads total roughly 100 KB and range from 144
to 1,138 vertices. The game's source prefabs, catch presentation, and asset
bundles are never modified.

## Location and water-height accuracy

The game already supplies both pieces needed for correct placement:

1. `FieldManager.ResidentData.GetAttributeParamList(fieldId,
   LayerAttribute.FishingPossible)` returns the current field's actual fishing
   areas and their `ParameterizedLayerAttributeFishingPossible.GroupId`.
2. The field's cell-centre conversion supplies X/Z and a layer-4-only downward
   raycast supplies the exact water height. This bypasses an Il2CppInterop bug
   that zeroes `Item1` of `GetFishingPoint`'s boxed `ValueTuple<Vector3,bool>`.

Version 0.14.0 splits each fishing attribute layer into four-directionally
connected water components, so disconnected ponds are independent pools. School
capacity is a bounded square-root function of that pool's grid cell count (4-12
fish with the supplied preset), rather than one fixed whole-map count. A pool is
initialized 26 metres from the player; its procedural silhouettes appear
synchronously and its exact native prefabs upgrade asynchronously. At 38 metres
the render objects unload but their master rows and positions remain. A caught
fish recovers after 120 seconds, one vacancy per interval. Hour, rod, and bait
changes do not reroll actors already in the school. Roaming uses retained
velocity, bounded yaw, acceleration, burst-and-coast speed targets, and only
selects the current or a connected neighboring fishing cell, preventing a
straight-line shortcut across an unvalidated bank. When one fish notices a
bobber, nearby fish in that same connected pool make short, faster steps toward
successively farther neighboring cells. They remain members of the finite
school: the reaction changes position and velocity, not population state.
The same connected-cell escape behavior is also used for shoreline disturbance.
The plugin checks the native `PlayerCharacter.IsRun()` state and measured player
displacement together, so ordinary walking, standing transitions, and field
warps do not trigger it. While the player is actually running, loaded fish within
4.25 metres receive at most one disturbance pulse every 0.75 seconds.

## Eligibility and catch synchronization

The native lottery takes the habitat group, rod level, in-game date/hour,
today's weather, wind level, and active bait. The preview implementation calls
the game's own `FishManager.ExecuteLottery` with those values, restoring
Unity's global random state afterward so merely displaying previews does not
advance normal game randomness. Non-fish results are excluded from the visible
fish population.

When a cast lands close to a displayed silhouette, the nearest same-habitat
fish pursues the bobber and its exact `FishingMasterData` row is reserved.
Native analysis and the 0.7.0 runtime exception show that the rod action calls
`FishWaitingStart` independently; suppressing `LotteryFish` therefore let the
state advance with a null `decideFish`. Version 0.14.0 lets the lottery complete
immediately and supplies the reserved row, making `decideFish` valid. Strict
mode retains `DecideFish` as a final safety gate, but native disassembly exposed
an important caller-side detail: `PlayerActionFishingRod.UpdateAction` continues
into its bite animation after invoking `DecideFish`, even when a Harmony prefix
skips that callee. Version 0.15.0 therefore parks the rod's `fishingHitTimer`
below `fishingHitTime` until the pursuing visible fish reaches its final bite,
then releases that same native transition. An off-target cast has a valid
internal waiting row, never enters the phantom catch sequence, and remains
natively cancelable. Every
matching lottery call receives the reservation until the lifecycle finishes.
The reservation remains fixed for the cast. Version 0.14.0 holds `DecideFish`
through 1-3 visible approach/retreat test nibbles, hides the silhouette, and
releases the native pipeline only on the final hook/splash. A non-null rod
result, `FinishFishing`, or `isMiniGameFishingWin` confirms consumption.
`EndFishing` is deliberately not treated as failure because runtime traces show
that it fires at the hook transition. Only true end/cancel paths without a win
reactivate the silhouette and make it accelerate away into the recovery queue.

This makes the system location-accurate and condition-accurate without changing
inventory or save data. Like Animal Crossing, the silhouette communicates a
shape/size class rather than revealing the species name. The actual catch is
synchronized only when the player targets a displayed silhouette.

## Reproducible evidence

`tools/extract_fish_data.py` decodes the installed Addressables catalog and
master-data bundle, producing the reports under `reverse-engineering/fish-data`.
`tools/extract_writable_fish_meshes.py` performs the separate local geometry
extraction and writes the five `.rfmesh` payloads plus their provenance manifest.
The current game data contains 190 condition rows, 67 unique catchable item IDs,
and eleven resolved silhouette model IDs. `Plugin.cs` builds directly against
the installed BepInEx IL2CPP interop assemblies rather than placeholder stubs.
