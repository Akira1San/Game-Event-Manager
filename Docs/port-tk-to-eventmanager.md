## Porting Priority & Implementation Plan

### High Priority - Core Gameplay (First)
1. **Player 8-direction movement** with collision
   - Implement using `move forward/back/left/right speed N`
   - Use `IEventWorld.IsTouching` or collision detection for block collisions
   - Reference: Existing `Player.events` in EventManager

2. **Enemy AI states** (patrol → chase → attack)
   - Create `Enemy.events` with mode system
   - Implement `sees Player by N` and `stops seeing Player` triggers
   - Reference: Existing `Enemy.events` in EventManager

3. **Line-of-sight detection**
   - Implement `HasLineOfSight` in `DefaultEventWorld`
   - Use `sees Player by N` trigger syntax
   - TK uses 180° FOV + raycast; EventManager has separate implementation

4. **Shooting mechanics**
   - `spawn Bullet at me` + `play sound "shot"`
   - Add bullet lifetime and despawn logic
   - Reference: Existing `Player.events` shooting

5. **Animation control**
   - `loop anim "Walk"` / `loop anim "Run"` / `play anim "Attack"`
   - Handle animation end events via `when anim "Name" ends`
   - Reference: Existing `Character.events`

### Medium Priority - Systems Enhancement
6. **Physics movement opt-in**
   - `UsePhysicsMovement` flag on `EventSheet`
   - When enabled: use `PhysicsMovement.DynamicVelocity`
   - When disabled: direct `Position` assignment (default)
   - Bridge TK's speed-based movement to physics velocities

7. **Sound system integration**
   - Map TK sound effects to Flax AudioSources
   - `play sound "shot"` → play Flax audio clip
   - Add 3D spatial audio for positional awareness

8. **Invulnerability/hurt state**
   - `set my Invulnerable to true/false` variable management
   - `hide me` / `show me` for brief invulnerability
   - `add -1 to Lives` on player death

9. **Weapon/ammo system**
   - Data-driven weapon types from TK's `iType->CurrentWeapon()`
   - Ammunition counting with multipliers
   - Reload/loading counters
## EventManager Architecture (Flax Engine)

The EventManager uses data-driven `.events` files attached to actors via `EventSheet` components.

### Mapped Correspondences

| TK Feature | EventManager Equivalent | Events Syntax |
|---|---|---|
| **8-dir Movement** | `move forward/back/left/right speed N` | `when W is held \n  move forward speed 45 \n  loop anim "Walk"` |
| **Rotation** | `set LookAtMouse` + FlaxActions `FaceTarget` | `when game starts \n  set LookAtMouse to 1` |
| **Enemy AI States** | `set my mode to "state"` + trigger conditions | `when me appears \n  set my mode to "patrol" \n  goto random Waypoint \n  loop anim "Walk"` |
| **Line-of-sight** | `sees Player by N` / `stops seeing Player` | `when me sees Player by 8 \n  set my mode to "chase" \n  loop anim "Run" \n  goto Player` |
| **Shooting** | `spawn Bullet at me` + `play sound` | `when Left Mouse is pressed \n  spawn Bullet at me \n  play sound "shot"` |
| **Global State** | `Game.events` sheet with globals | `when game starts \n  set Score to 0 \n  set Lives to 3` |
| **Physics Movement** | `UsePhysicsMovement` flag | `[UsePhysicsMovement] \n move forward speed 45` |
| **Waypoints** | `goto random Waypoint` / `when me arrives at Waypoint` | `when me appears \n  goto random Waypoint` |
| **Animation Control** | `loop anim "Name"` / `play anim "Name"` | `loop anim "Walk"` / `play anim "Attack"` |
| **Distance Checks** | `when me is near Player by 1.5` | `when me is near Player by 1.5 \n  play anim "Attack" \n  send message "hurt" to Player` |
| **Hurt/Invulnerability** | `unless my Invulnerable is true` + `hide/show` | `when message "hurt" arrives \n  unless my Invulnerable is true \n  play sound "hurt.wav" \n  add -1 to Lives \n  set my Invulnerable to true \n  hide me \n  wait 1 second \n  show me \n  set my Invulnerable to false` |

## TK Game Architecture

The TK game (SDL/C++) consists of these core subsystems:

### 1. Character Movement (CPlayer, CCharacter)
- 8-directional movement (WASD) with collision detection against tile-based levels
- Footprint particle effects at movement endpoints
- Animation state management (walk/run/idle)
- Speed variants (normal: 45 units, shift-boosted: 90 units)

### 2. Enemy AI (CEnemy)
- States: Wander, Assault, Still
- Behavior: Patrol → Chase → Attack loop
- Line-of-sight: 180-degree FOV + raycast visibility
- Hearing: Can hear gunshots within sight distance
- Shooting: Targets nearest player in assault mode
- Speed: Type-dependent

### 3. Game States (CGameState)
- Level progression system
- Cooperative and deathmatch modes
- Statistics tracking (kills, enemies per type)

### 4. Weapons & Ammo (CBullet, CWeaponBase)
- Multiple weapon types with different firing patterns
- Ammunition management with multipliers
- Reloading/loading counters
- Bullet distance approximation for combat

### 5. Rendering (CGraphics, CGameDrawManager)
- Sprite-based 2D rendering
- Animation frame management
- Color indexing system

### 6. Audio (CSoundPlayer)
- Sound effect management
- Footstep sounds, shooting, explosions, death cries

### 7. Level Data (CLevel)
- Tile-based format (20px blocks)
- Collision types: floor, walls, impassable
- Visibility checking for line-of-sight
- Dust block tracking for footstep effects