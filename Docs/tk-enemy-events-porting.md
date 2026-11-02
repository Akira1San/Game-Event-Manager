## Enemy AI States

TK enemy has 3 states: Wander (patrol), Assault (chase), Still. States controlled by `my mode` variable.

## Patrol / Wander Behavior

TK wander: `goto random Waypoint` pathfinds around automatically. `iWalkCounter` counts down based on distance/speed. 1% chance per tick (`rand()%125==1`) to pick new waypoint.

### EventManager Equivalent
## Line-of-Sight Detection

TK `CheckPlayerVisibility()`: 180-degree FOV (`iAngle ± 90`), raycast from center to player center, distance check against `iType->SightDistance()`, `VisibilityCheck()` for line-of-sight through level.

### EventManager Equivalent
## Chase Behavior

TK: After seeing player, switch to `EAssault` state. `goto Player` constantly re-targets moving player. `every 0.2 seconds` re-checks `my mode is "chase"` && `goto Player`. `CheckForShoot()` attempts to shoot when near.

### EventManager Equivalent
## Attack When Near Player

TK `CheckForShoot()`: When distance ≤ `ApproxDistance() + 20`, increment `iShootCount` and `wannashoot=1`. Shoot when `iLoadCounter == LoadingTime()`. Bullet type determines distance/tolerance.

### EventManager Equivalent
```events
# Attack when close to player
when me is near Player by 1.5
  my mode is "chase"
  play anim "Attack"
  send message "hurt" to Player
```
```events
# Chase re-targeting every tick
when every 0.2 seconds
  my mode is "chase"
  goto Player
```
```events
# Spot player by distance
when me sees Player by 8
  set my mode to "chase"
  loop anim "Run"
  goto Player

# Lose player - stops seeing
when me stops seeing Player
  set my mode to "patrol"
```
```events
# Waypoint navigation
when me arrives at Waypoint
  wait random 0.5 to 2 seconds
  goto random Waypoint
```
### TK Code (CEnemy)
- `EWanderAround` - random waypoint navigation
- `EAssault` - hunt nearest player
- `EStill` - stop moving
- State transitions via `iState = EXxx`

### EventManager Equivalent
```events
# Appear and start patrolling
when me appears
  set my mode to "patrol"
  goto random Waypoint
  loop anim "Walk"
```
# TK Enemy Events Porting Documentation

This file documents porting TK enemy AI events to EventManager (Flax Engine).