using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Timers;

namespace Реалізація_спеціальної_шини_подій_із_обмеженням
{
   class Program
    {
        static async Task Main(string[] args)
        {
            //start tasks
            Task GameTask = Game();
            Task KBListenerTask = KeyboardListener();

            //await GameTask;
            await KBListenerTask;

            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
        }

        public static async Task Game()
        {
            await Task.Run(() =>
            {
                // 1. create our various game systems
                SoundManager soundManager = new SoundManager();
                UIManager uiManager = new UIManager();
                GameManager gameManager = new GameManager();

                // 2. register event subscribers
                EventBus.Event_Bus.Subscribe("OnLevelUp", soundManager.OnLevelUp);
                EventBus.Event_Bus.Subscribe("OnMuteUnmute", soundManager.OnMuteUnmute);
                EventBus.Event_Bus.Subscribe("OnLevelUp", uiManager.ShowLevelUpBaner);
                EventBus.Event_Bus.Subscribe("OnLevelUp", uiManager.ShowCurrentLevelLabel);
                EventBus.Event_Bus.Subscribe("KeyPressed", uiManager.LongRun_KeyPress);
                EventBus.Event_Bus.Subscribe("ReceivedCommand", gameManager.CommandAnalyzer);

                Console.WriteLine("\nPress  'Q'  to Exit the game or  'H'  for help ");
    
                while (!gameManager.GameOver)
                {
                    //Thread.Sleep(100);
                }

                Console.WriteLine("\n\nGame over...");
            });
        }
        public static async Task KeyboardListener()
        {
            ConsoleKeyInfo key = new ConsoleKeyInfo();
            Console.WriteLine("Press ESC to abort KeyboardListener task");
            await Task.Run(() =>
            {
                while (!Console.KeyAvailable && key.Key != ConsoleKey.Escape)
                {
                    key = Console.ReadKey(true);
                    GameEventArgs e = new GameEventArgs();
                    e.KeyInfo = key;

                    if(!EventBus.Event_Bus.Publish("KeyPressed", e))
                    {
                        EventBus.Event_Bus.NotifyEventQueueIsFull("KeyboardListener", "KeyPressed");
                    }
                }
            });
        }

        public class GameEventArgs : EventArgs
        {
            public int Level { get; set; }
            public ConsoleKeyInfo KeyInfo { get; set; }
        }

        public class EventBus
        {
            private int BusFrequencyPeriod = 500;// 500 mSec
            private int MaxQueueLength = 5;

            public static readonly EventBus Event_Bus = new EventBus();
            readonly Dictionary<String, EventBox> _subscriptions = new Dictionary<String, EventBox>();

            //multithreading safe FIFO queue
            ConcurrentQueue<EventQueueElement> cq = new ConcurrentQueue<EventQueueElement>();
            private Timer timer = new Timer();
 
            public EventBus()
            {
                timer = new Timer();
                timer.Interval = BusFrequencyPeriod;
                timer.AutoReset = true;// Enable recurrent events.
                timer.Elapsed += (sender, eventArgs) => OnTimer();
                timer.Start();
            }

            private void OnTimer()
            {
                if(!cq.IsEmpty)
                {
                    if(cq.TryDequeue(out EventQueueElement currenEvent))
                    {
                        EventBox _evBox = currenEvent.EvBox;
                        GameEventArgs _evArgs = currenEvent.EvArgs;
                        _evBox.Publish(_evArgs);
                    }
                }
            }

            public void Subscribe(string EvName, EventHandler<GameEventArgs> d)
            {
                if (!_subscriptions.TryGetValue(EvName, out EventBox forEvBox))
                {
                    forEvBox = new EventBox();
                    _subscriptions.Add(EvName, forEvBox);
                }

                forEvBox.Event += d;
                //Console.WriteLine("[EventBus] event {0} subscribed", EvName);
            }
            public void UnSubscribe(string EvName, EventHandler<GameEventArgs> d)
            {
                if (!_subscriptions.TryGetValue(EvName, out EventBox forEvBox))
                {
                    _subscriptions.Remove(EvName);
                }
                else
                {
                    forEvBox.Event -= d;
                }
            }
            public bool Publish(string EvName, GameEventArgs e)
            {
                if (!_subscriptions.TryGetValue(EvName, out EventBox forEvBox))
                {
                    forEvBox = new EventBox();
                    _subscriptions.Add(EvName, forEvBox);
                   // Console.WriteLine("[EventBus] event {0} added",EvName);

                }

                if (cq.Count < MaxQueueLength)
                {
                    cq.Enqueue(new EventQueueElement(forEvBox, e));
                    return true;
                }
                else
                {
                    return false;
                }
                //forEvBox.Publish(e);
                //Console.WriteLine("[EventBus] event {0} published", EvName);
            }

            public void NotifyEventQueueIsFull(string sender, string eventName)
            {
                Console.WriteLine("\n[{0}] can't enqueue event '{1}'\n",sender,eventName);
            }
        }

        public class EventBox
        {
            public event EventHandler<GameEventArgs> Event;

            public void Publish(GameEventArgs e)
            {
                EventHandler<GameEventArgs> handler = Event;

                if (handler != null)
                {
                    this.Event.DynamicInvoke(this,e);

                    //handler(this, e);
                    //Console.WriteLine("[EventBox] event fired");
                }
                else
                {
                    Console.WriteLine("[EventBox] didn't find any event handler");
                }
            }
        }

        public class EventQueueElement
        {
            public EventBox EvBox;
            public GameEventArgs EvArgs;

            public EventQueueElement(EventBox evBox, GameEventArgs e)
            {
                this.EvBox = evBox;
                this.EvArgs = e;
            }
        }
        public class GameManager
        {
            internal int level = 1;
            internal bool GameOver = false;

            public void LevelUp()
            {
                level += 1;
                Console.WriteLine("\n[Game] Current level: {0}", level);
                GameEventArgs e = new GameEventArgs();
                e.Level = level;

                if (!EventBus.Event_Bus.Publish("OnLevelUp", e))
                {
                    EventBus.Event_Bus.NotifyEventQueueIsFull("Game", "OnLevelUp");
                }
            }

            public void CommandAnalyzer(object sender, GameEventArgs e)
            {
               // Console.WriteLine("\n[CommandAnalyzer] key {0} received",e.KeyInfo.Key);
                if (e.KeyInfo.Key == ConsoleKey.Q)
                {
                    GameOver = true;
                }
                else if (e.KeyInfo.Key == ConsoleKey.M)
                {
                    //Console.WriteLine("[CommandAnalyzer] trying to mute/unmute", e.KeyInfo.Key);
                    if (!EventBus.Event_Bus.Publish("OnMuteUnmute", e))
                    {
                        EventBus.Event_Bus.NotifyEventQueueIsFull("CommandAnalyzer", "OnMuteUnmute");
                    }
                }
                else if (e.KeyInfo.Key == ConsoleKey.L)
                {
                    LevelUp();
                }
                else if (e.KeyInfo.Key == ConsoleKey.H)
                {
                    Console.WriteLine("\n[CommandAnalyzer] Help page");
                    Console.WriteLine("Hot keys (commands):");
                    Console.WriteLine("H - help (this page)");
                    Console.WriteLine("L - Level up command");
                    Console.WriteLine("M - mute/unmute sound");
                    Console.WriteLine("Q - exit the game");
                }
                else if (e.KeyInfo.Key == ConsoleKey.L)
                {
                    LevelUp();
                }
                else
                { 
                    Console.WriteLine("\n[CommandAnalyzer] Unknown Command.");
                }
            }
        }

        public class SoundManager
        {
            private bool Muted = false;
            public void OnLevelUp(object sender, GameEventArgs e)
            {
                if (!Muted)
                {
                    Console.WriteLine("[SOUND] Levelling up");
                }
            }
            public void OnMuteUnmute(object sender, GameEventArgs e)
            {
                Muted = !Muted;
                Console.WriteLine("\n[SOUND] Sound system {0}" ,(Muted) ? "muted" : "unmuted");
            }
        }

        public class UIManager
        {
            public void ShowLevelUpBaner(object sender, EventArgs e)
            {
                Console.WriteLine("[UI] Levelling up");
            }
            public void ShowCurrentLevelLabel(object sender, GameEventArgs e)
            {
                Console.WriteLine("[UI] Current level: {0}", e.Level);
            }
            public void LongRun_KeyPress(object sender, GameEventArgs e)
            {
                Console.WriteLine("[UI] key {0} pressed", e.KeyInfo.Key);
                if (!EventBus.Event_Bus.Publish("ReceivedCommand", e))
                {
                    EventBus.Event_Bus.NotifyEventQueueIsFull("UI", "ReceivedCommand");
                }
            }
        }
    }
}
