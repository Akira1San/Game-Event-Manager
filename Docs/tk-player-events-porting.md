# TK Player Events Porting Documentation

This file documents porting TK player events to EventManager (Flax Engine).
## Player Movement

TK 8-directional movement with collision maps to EventManager `move forward/back/left/right speed N`.

### TK Code (CCharacter::Move)
- 8 collision points checked around character bounds
- Footprint effects at movement endpoints
- Walk animation via iWalk counter

### EventManager Equivalent
```events
when W is held
  unless Left Shift is held
  move forward speed 45
  loop anim "Walk"

when S is held
  unless Left Shift is held
  move back speed 45
  loop anim "Walk"

when A is held
  unless Left Shift is held
  move left speed 50
  loop anim "Walk"

when D is held
  unless Left Shift is held
  move right speed 50
  loop anim "Walk"

when Left Shift is held
  W is held
  move forward speed 90

when Left Shift is held
  S is held
  move back speed 90

when Left Shift is held
  A is held
  move left speed 90

when Left Shift is held
  D is held
  move right speed 90
## Player Rotation

## Player Shooting

## Player Hurt/Invulnerability

TK hurt state: `when message "hurt" arrives` → check `unless my Invulnerable is true` → `play sound "hurt.wav"` → `add -1 to Lives` → `set my Invulnerable to true` → `hide me` → `wait 1 second` → `show me` → `set my Invulnerable to false`.

### TK Code Flow
- Invulnerability flag checked before processing hurt
- 1-second invulnerability period
- Sprite hides/shows during invulnerability
- Lives decremented

### EventManager Equivalent
```events
# Hurt state management
when message "hurt" arrives
  unless my Invulnerable is true
  play sound "hurt.wav"
  add -1 to Lives
  set my Invulnerable to true
  hide me
  wait 1 second
  show me
  set my Invulnerable to false
```
TK primary fire: `spawn Bullet at me` + `play sound "shot"`. Space also fires.

### TK Code (CPlayer::Shoot logic)
- Bullet spawned at player center (+14, +14 offset)
- Bullet type from `iCurrentWeapon->BulletType()`
- Distance based on bullet type `ApproxDistance()`
- Load counter tracks reloading

### EventManager Equivalent
```events
# Primary fire - Left Mouse
when Left Mouse is pressed
  spawn Bullet at me
  play sound "shot"

# Secondary fire - Space
when Space is pressed
  spawn Bullet at me
  play sound "shot"
```
TK uses angle-based facing (`iAngle`, `iTargetAngle`) with mouse-looking opt-in.

### TK Code
- `iAngle` stores current facing direction (0-359)
- `iTargetAngle` stores target direction
- `TargetAngle()` method interpolates toward target
- `set LookAtMouse to 1` opt-in for mouse aiming

### EventManager Equivalent
```events
# Mouse-looking opt-in
when game starts
  set LookAtMouse to 1
```
```