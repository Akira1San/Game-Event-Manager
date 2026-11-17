using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>What <see cref="LiftOffManager.TryLand"/> decided.</summary>
    public enum LandResult
    {
        /// <summary>The building is down again; its queues resume.</summary>
        Landed,
        /// <summary>It was never airborne, so there is nothing to land.</summary>
        NotFlying,
        /// <summary>The ground under it is not one its own unit type may stand on (water under a barracks).</summary>
        GroundRefused,
        /// <summary>Another building already owns the grid cells it would land on.</summary>
        GridRefused,
    }

    /// <summary>One building's flight state, as <c>lift off</c> left it.</summary>
    public class LiftOffState
    {
        /// <summary>The building template name (<c>Barracks</c>), matched by the land trigger.</summary>
        public string Building = "";
        /// <summary>The height it rose to — what <c>my Altitude</c> holds.</summary>
        public double Height;
        /// <summary>True when it held grid cells before it lifted, so landing re-reserves them.</summary>
        public bool Reserved;
        /// <summary>True while it is up.</summary>
        public bool Flying;
    }

    /// <summary>
    /// RTS lift-off and landing for buildings (Task 28 Part C): a building rises to its
    /// <c>my Altitude</c>, pauses its production and research queues and frees its build-grid
    /// footprint; <c>land</c> puts it back down, refuses ground its type cannot stand on (or a
    /// footprint someone else took meanwhile) and resumes the work it paused.
    ///
    /// Pure logic with no Flax types — the bridge supplies the ground test and the grid dry run,
    /// which is why <see cref="TryLand"/> takes two answers instead of a world. The height itself
    /// is the ordinary per-unit variable Part A already pins; this class only decides when to
    /// write it and what else changes with it.
    /// </summary>
    public class LiftOffManager
    {
        /// <summary>Global that sets the rise height when the sheet wrote neither a number nor an altitude.</summary>
        public const string HeightVariable = "LiftOffHeight";

        /// <summary>Default rise height in world units: clearly off the ground, still on screen.</summary>
        public const double DefaultHeight = 200.0;

        private readonly Dictionary<object, LiftOffState> _states = new Dictionary<object, LiftOffState>();

        /// <summary>
        /// The height <c>lift off</c> rises to: the number the word carries, else the
        /// <see cref="HeightVariable"/> global, else <see cref="DefaultHeight"/>. A missing or
        /// unreadable global never disables lifting.
        /// </summary>
        public static double ResolveHeight(EventVariables variables, double requested)
        {
            if (requested > 0)
                return requested;
            if (variables != null && variables.HasGlobal(HeightVariable))
            {
                double global = variables.GetGlobal(HeightVariable).AsNumber();
                if (global > 0)
                    return global;
            }
            return DefaultHeight;
        }

        public bool IsFlying(object actor)
        {
            return actor != null && _states.TryGetValue(actor, out LiftOffState state) && state.Flying;
        }

        /// <summary>The height it lifted to, or 0 when it never lifted.</summary>
        public double HeightOf(object actor)
        {
            return actor != null && _states.TryGetValue(actor, out LiftOffState state) ? state.Height : 0;
        }

        /// <summary>The template name it lifted under, or "" when it never lifted.</summary>
        public string BuildingOf(object actor)
        {
            return actor != null && _states.TryGetValue(actor, out LiftOffState state) ? state.Building : "";
        }

        /// <summary>True when the building held grid cells at take-off, so landing re-reserves them.</summary>
        public bool ReservedOf(object actor)
        {
            return actor != null && _states.TryGetValue(actor, out LiftOffState state) && state.Reserved;
        }

        public int FlyingCount
        {
            get
            {
                int count = 0;
                foreach (var pair in _states)
                    if (pair.Value.Flying)
                        count++;
                return count;
            }
        }

        /// <summary>
        /// Raises the building: writes <c>my Altitude</c> (Part A pins it there), frees its grid
        /// cells, pauses production and research, and raises <c>LiftOff</c> with the template in
        /// <c>Name</c> and the actor in <c>Other</c>. False when it is already flying.
        /// </summary>
        public bool TryLift(EventVariables variables, EventRuntime runtime, object actor,
                            string building, double requestedHeight, bool reservedCells)
        {
            if (actor == null || IsFlying(actor))
                return false;

            double height = ResolveHeight(variables, requestedHeight);
            _states[actor] = new LiftOffState
            {
                Building = building ?? "",
                Height = height,
                Reserved = reservedCells,
                Flying = true,
            };
            variables?.SetLocal(actor, Altitude.Variable, ValueBox.OfNumber(height));
            runtime?.Construction.ReleaseCells(actor);
            runtime?.Production.SetPaused(actor, true);
            runtime?.Research.SetPaused(actor, true);
            runtime?.RaisePushAll(new PushSignal
            {
                Kind = TriggerKind.LiftOff,
                Name = building ?? "",
                Other = actor,
            });
            return true;
        }

        /// <summary>
        /// Puts the building down. <paramref name="groundPassable"/> is whether its own unit type
        /// may stand where it is, and <paramref name="gridFree"/> whether its footprint can be
        /// re-reserved; either refusal leaves it flying and raises <c>when path blocked</c> with
        /// the name <c>land</c> on the building's own sheet. A successful landing writes
        /// <c>my Altitude</c> to 0 (Part A settles it on that ground), resumes both queues and
        /// raises <c>Lands</c>.
        ///
        /// An actor that only holds an altitude — lifted by <c>fly to … at …</c> rather than by
        /// <c>lift off</c> — can land too; it simply has no paused work to resume.
        /// </summary>
        public LandResult TryLand(EventVariables variables, EventRuntime runtime, object actor,
                                  bool groundPassable, bool gridFree)
        {
            if (actor == null)
                return LandResult.NotFlying;

            bool tracked = _states.TryGetValue(actor, out LiftOffState state) && state.Flying;
            if (!tracked && Altitude.Of(variables, actor) <= 0)
                return LandResult.NotFlying;

            if (!groundPassable)
            {
                RefuseLanding(runtime, actor);
                return LandResult.GroundRefused;
            }
            if (state != null && state.Reserved && !gridFree)
            {
                RefuseLanding(runtime, actor);
                return LandResult.GridRefused;
            }

            string building = state != null && state.Building.Length > 0
                ? state.Building
                : runtime?.Construction.BuildingOf(actor) ?? "";
            if (state != null)
            {
                state.Flying = false;
                runtime?.Production.SetPaused(actor, false);
                runtime?.Research.SetPaused(actor, false);
            }
            variables?.SetLocal(actor, Altitude.Variable, ValueBox.OfNumber(0));
            runtime?.RaisePushAll(new PushSignal
            {
                Kind = TriggerKind.Lands,
                Name = building,
                Other = actor,
            });
            return LandResult.Landed;
        }

        /// <summary>Drops a destroyed building's flight state.</summary>
        public void Forget(object actor)
        {
            if (actor != null)
                _states.Remove(actor);
        }

        /// <summary>Clears every building's flight state for a scene restart / new session.</summary>
        public void Reset()
        {
            _states.Clear();
        }

        private static void RefuseLanding(EventRuntime runtime, object actor)
        {
            runtime?.RaisePush(actor, new PushSignal
            {
                Kind = TriggerKind.PathBlocked,
                Name = "land",
                Other = actor,
            });
        }
    }
}
