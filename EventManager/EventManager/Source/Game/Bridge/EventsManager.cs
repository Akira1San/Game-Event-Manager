using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Game
{
    public class EventsManager : Script
    {
        public static EventsManager Instance;
        private static bool _gameStartFiredThisSession;

        public EventRuntime Runtime = new EventRuntime();
        private readonly FlaxWorld _world = new FlaxWorld();
        private readonly Dictionary<string, bool> _heldLastTick = new Dictionary<string, bool>();

        public void HandleTouch(Actor me, Actor other, bool leaving)
        {
            if (other == null)
                return;
            Runtime.PushContact(leaving ? TriggerKind.Overlaps : TriggerKind.Touches, me, other);
        }

        public override void OnStart()
        {
            Instance = this;
            FlaxActions.Warn = FlaxActions.WarnOnce;
            Runtime.World = _world;
            Runtime.OnRuntimeWarning = message => Debug.LogWarning("Event sheet: " + message);
            Runtime.OnRawAction = (context, words) =>
                FlaxActions.Execute(context, ActionWords.Parse(words, CancelOneTime));
            Runtime.OnSetText = (context, thing, value) =>
            {
                FlaxActions.SetText(thing, value.AsText());
            };

            string folder = EventFiles.LocateGameFiles(System.IO.Directory.GetCurrentDirectory())
                ?? EventFiles.LocateGameFiles(AppContext.BaseDirectory);
            if (folder == null)
            {
                Debug.LogWarning("I couldn't find a GameFiles folder with .events files — events won't run this scene.");
                return;
            }
            var programs = EventFiles.LoadAll(folder);
            foreach (EventFileResult program in programs)
            {
                if (!string.IsNullOrEmpty(program.Error))
                {
                    Debug.LogWarning($"Event sheet: I found a problem in {System.IO.Path.GetFileName(program.Path)} and I'm skipping it for now:\n    {program.Error}");
                    continue;
                }
                if (program.Name == "Game")
                {
                    Runtime.AddSheet(new GlobalHost(), program.File);
                    continue;
                }
                foreach (Actor actor in SceneActors())
                {
                    if (actor.Name == program.Name)
                    {
                        Runtime.AddSheet(actor, program.File);
                        StickerTo(actor);
                    }
                }
            }

            if (!_gameStartFiredThisSession)
            {
                _gameStartFiredThisSession = true;
                Runtime.SessionStarted();
            }
            Runtime.SceneStarted();
        }

        private void CancelOneTime(string message)
        {
            FlaxActions.WarnOnce("Event sheet: " + message);
        }

        public override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            FlaxActions.ClearSession();
            Runtime.RemoveAllSheets();
        }

        public override void OnUpdate()
        {
            float delta = (float)(Time.DeltaTime > 0 ? Time.DeltaTime : 0.0001);
            FlaxActions.DeltaTime = delta;
            InputEdges();
            Runtime.Tick(Time.GameTime);
        }

        private void InputEdges()
        {
            foreach (string key in ActionWords.KeyNamesForScanning)
            {
                bool held = _world.IsKeyHeld(key);
                _heldLastTick.TryGetValue(key, out bool wasHeld);
                if (held && !wasHeld)
                {
                    var press = new PushSignal { Kind = TriggerKind.KeyPressed, Name = key };
                    Runtime.RaisePushAll(press);
                }
                else if (wasHeld && !held)
                {
                    Runtime.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyReleased, Name = key });
                }
                _heldLastTick[key] = held;
            }
            float wheel = Input.MouseScrollDelta;
            if (wheel > 0)
                Runtime.RaisePushAll(new PushSignal { Kind = TriggerKind.WheelScrollUp });
            else if (wheel < 0)
                Runtime.RaisePushAll(new PushSignal { Kind = TriggerKind.WheelScrollDown });
        }

        private static void StickerTo(Actor actor)
        {
            var sticker = actor.AddScript<EventSheet>();
            sticker?.Setup(Instance);
        }

        private static List<Actor> SceneActors()
        {
            var found = new List<Actor>();
            var scenes = Level.Scenes;
            for (int s = 0; s < scenes.Length; s++)
            {
                var scene = scenes[s];
                if (scene == null)
                    continue;
                Collect(scene, found);
            }
            return found;
        }

        private static void Collect(Actor node, List<Actor> found)
        {
            foreach (Actor child in node.Children)
            {
                if (child == null)
                    continue;
                found.Add(child);
                Collect(child, found);
            }
        }
    }
}
