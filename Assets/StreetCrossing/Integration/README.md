# StreetCrossing Full-Body Integration

`StreetCrossingFullBodyBinding.cs` glues the VirtualHand full-body avatar
(`Assets/VirtualHand/Prefabs/FirstPersonFullBody.prefab`) onto the StreetCrossing player
at runtime:

- A `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` bootstrap creates a
  `DontDestroyOnLoad` host GameObject that owns the binding component.
- `EnsureWiring()` instantiates the prefab under the active `PedestrianPlayerController`
  root and adds a `GameIntegrationAdapter` (the prefab intentionally ships without it).
- `LateUpdate` feeds root-motion-derived locomotion (planar velocity + yaw rate) into the
  adapter; keyboard **B** toggles first-person visibility, **V** cycles the appearance
  mode.

## Gesture recognition stack: scripts ship, but nothing is wired at runtime

The hand-gesture recognition scripts *are* present in this project under
`Assets/VirtualHand/Scripts/Runtime` (`Integration/VirtualHandHost`,
`Input/HandInputRouter`, `Interaction/HandInteractionRouter`,
`Interaction/HandGestureRecognizer`, plus the `Input/` and `Tracking/` pose providers);
the script inventory is identical to the Game04-VirtualHand source worktree. What Game03
does **not** have is any *runtime wiring* for them:

- The `FirstPersonFullBody` prefab ships without a `VirtualHandHost`, without a hand pose
  provider, and — by design, see `FirstPersonFullBodyBuilder.cs` — without the
  `GameIntegrationAdapter` itself. The binding adds only the adapter.
- Nothing in a Game03 scene or runtime path instantiates `VirtualHandHost` or any
  `IHandPoseProvider`, and nothing calls
  `GameIntegrationAdapter.OnHandUpdate(...)`.

Consequences:

- `GameIntegrationAdapter.MapGesture` (gesture → whole-body pose, e.g. pointing to
  "LeftArmForward"/"RightArmForward") is never invoked. It is a dormant **seam** kept for
  future wiring: to enable gestures, add a `VirtualHandHost`-driven pipeline (pose
  provider → recognizer → host) and pass it the adapter so `OnHandUpdate` receives hand
  frames and gesture events.
- Game input is unaffected: fast walk (**Shift**) and interact (**E**) are served by
  `LegacyStreetInput` (`Assets/StreetCrossing/Scripts/Player/IStreetInput.cs`), which is
  independent of the full-body binding. The binding deliberately binds only **B**/**V**
  so it never collides with the host's keys.

## DontDestroyOnLoad behaviour across scene transitions

- The bootstrap **host GameObject** (`StreetCrossingFullBodyBinding`) is marked
  `DontDestroyOnLoad` in `Bootstrap()`, so the binding component survives every scene
  load and keeps polling from `Update`.
- The **body instance is not** `DontDestroyOnLoad`: it is parented under the scene's
  `PedestrianPlayerController` root, so it is destroyed together with the scene.
- On the next scene load, `EnsureWiring()` — called from `Update` every frame — sees
  `IsWired == false` and re-creates the body under the new player, re-adds the adapter
  when needed and re-primes the locomotion baseline. `EnsureWiring()` is idempotent
  (early return while fully wired plus per-part null checks), so no duplicate parents or
  bodies are created across scene loads or component re-enable cycles.
- Because re-wiring re-applies the initial state, the **B/V toggle state resets to the
  defaults** (first-person head hidden, Realistic appearance) after a scene change.
- If the active scene has no `PedestrianPlayerController`, `EnsureWiring()` returns
  `false` and retries each frame; no body is instantiated until a player exists.
