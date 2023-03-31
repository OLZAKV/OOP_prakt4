using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;

namespace Запровадити_шаблон_публікації_підписки_з_пріоритетами
{
    class Program
    {

        static async Task Main(string[] args)
        {
            //start tasks
            Task GameTask = GameAsync();
            Task KBListenerTask = KeyboardListenerAsync();

            //await GameTask;
            await KBListenerTask;

            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
        }

        public static async Task GameAsync()
        {
            await Task.Run(() =>
            {
                // 1. create our various game systems
                SoundManager soundManager = new SoundManager();
                UIManager uiManager = new UIManager();
                GameManager gameManager = new GameManager();
                Battery battery = new Battery();
                Laser laser = new Laser();

                // 2. register event subscribers
                EventBus.Event_Bus.Subscribe("OnLevelUp", soundManager.OnLevelUp);
                EventBus.Event_Bus.Subscribe("OnMuteUnmute", soundManager.OnMuteUnmute);
                EventBus.Event_Bus.Subscribe("OnLevelUp", uiManager.ShowLevelUpBaner);
                EventBus.Event_Bus.Subscribe("OnLevelUp", uiManager.ShowCurrentLevelLabel);
                EventBus.Event_Bus.Subscribe("KeyPressed", uiManager.LongRun_KeyPress);
                EventBus.Event_Bus.Subscribe("ReceivedCommand", gameManager.CommandAnalyzer);
                EventBus.Event_Bus.Subscribe("OnLaserData", gameManager.OnLaserData);

                EventBus.Event_Bus.Subscribe("OnLaserFire", battery.OnLaserFire);

                EventBus.Event_Bus.Subscribe("OnWeaponFire", laser.Fire);
                EventBus.Event_Bus.Subscribe("OnBatteryData", laser.OnBatteryData);


                Console.WriteLine("\nPress  'Q'  to Exit the game or  'H'  for help ");

                while (!gameManager.GameOver)
                {
                    //Thread.Sleep(100);
                }
                Console.WriteLine("\n\nGame over...");
            });
        }

        public static async Task KeyboardListenerAsync()
        {
            ConsoleKeyInfo key = new ConsoleKeyInfo();
            Console.WriteLine("Press ESC to abort KeyboardListener task");
            await Task.Run(() =>
            {
                while (!Console.KeyAvailable && key.Key != ConsoleKey.Escape)
                {
                    key = Console.ReadKey(true);
                    var e = new GameEventArgs();
                    e.KeyInfo = key;

                    if (!EventBus.Event_Bus.Publish("KeyPressed", e, 4))
                    {
                        EventBus.Event_Bus.NotifyEventQueueIsFull("KeyboardListener", "KeyPressed");
                    }
                }
            });
        }

        private static object TryGetData(string ParamName, GameEventArgs e)
        {
            Hashtable _cp = e.CustomParameters;
            if (_cp.ContainsKey(ParamName))
            {
                return _cp[ParamName];
            }
            else return null;
        }

        public class GameEventArgs : EventArgs
        {
            public int Level { get; set; }
            public ConsoleKeyInfo KeyInfo { get; set; }
            public Hashtable CustomParameters { get; set; }
        }

        public class EventBus
        {
            private int BusFrequencyPeriod = 50;// mSec
            private int MaxQueueLength = 5;
            private static int MaxPriorityLevel = 5;

            public static readonly EventBus Event_Bus = new EventBus();
            readonly Dictionary<String, EventBox> _subscriptions = new Dictionary<String, EventBox>();

            //multithreading safe FIFO queue
            //ConcurrentQueue<EventQueueElement> cq = new ConcurrentQueue<EventQueueElement>();
            //string[] stringArray = new string[6];
            ConcurrentQueue<EventQueueElement>[] cqArray = new ConcurrentQueue<EventQueueElement>[MaxPriorityLevel];
            private Timer timer;

            public EventBus()
            {
                timer = new Timer();
                timer.Interval = BusFrequencyPeriod;
                timer.AutoReset = true;// enable recurrent events
                timer.Elapsed += (sender, eventArgs) => OnTimer();
                timer.Start();

                for (int i = 0; i < cqArray.Length; i++)
                {
                    cqArray[i] = new ConcurrentQueue<EventQueueElement>();
                }

            }
            private void OnTimer()
            {
                foreach (ConcurrentQueue<EventQueueElement> cq in cqArray)
                {
                    if (cq != null && !cq.IsEmpty)
                    {
                        if (cq.TryDequeue(out EventQueueElement currenEvent))
                        {
                            EventBox _evBox = currenEvent.EvBox;
                            GameEventArgs _evArgs = currenEvent.EvArgs;
                            _evBox.Publish(_evArgs);
                            break;
                        }
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
            public bool Publish(string EvName, GameEventArgs e, int Priority = 0)
            {
                if (!_subscriptions.TryGetValue(EvName, out EventBox forEvBox))
                {
                    forEvBox = new EventBox();
                    _subscriptions.Add(EvName, forEvBox);
                }

                if (cqArray[Priority].Count < MaxQueueLength)
                {
                    //cq.Enqueue(new EventQueueElement(forEvBox, e));
                    cqArray[Priority].Enqueue(new EventQueueElement(forEvBox, e));
                    return true;
                }
                else
                {
                    return false;
                }
            }
            public void NotifyEventQueueIsFull(string sender, string eventName)
            {
                Console.WriteLine("\n[{0}] can't enqueue event '{1}'\n", sender, eventName);
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
                    this.Event.DynamicInvoke(this, e);

                    //handler(this, e);
                    //Console.WriteLine("[EventBox] event fired");
                }
                else
                {
                    //Console.WriteLine("[EventBox] didn't find any event handler");
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
            bool ShowWeaponTemperature = false;

            public void LevelUp()
            {
                level += 1;
                Console.WriteLine("\n[Game] Current level: {0}", level);
                GameEventArgs e = new GameEventArgs();
                e.Level = level;

                if (!EventBus.Event_Bus.Publish("OnLevelUp", e, 3))
                {
                    EventBus.Event_Bus.NotifyEventQueueIsFull("Game", "OnLevelUp");
                }
            }

            public void CommandAnalyzer(object sender, GameEventArgs e)
            {
                Console.WriteLine("\n[CommandAnalyzer] key {0} received", e.KeyInfo.Key);
                if (e.KeyInfo.Key == ConsoleKey.H)
                {
                    Console.WriteLine("\n[CommandAnalyzer] Help page");
                    Console.WriteLine("Hot keys (commands):");
                    Console.WriteLine("H - help (this page)");
                    Console.WriteLine("L - Level up command");
                    Console.WriteLine("M - mute/unmute sound");
                    Console.WriteLine("S - show/hide laser data");
                    Console.WriteLine("W - select weapon: gun/cannon/laser");
                    Console.WriteLine("spacebar - fire");
                    Console.WriteLine("Q - exit the game");
                }
                else if (e.KeyInfo.Key == ConsoleKey.L)
                {
                    LevelUp();
                }
                else if (e.KeyInfo.Key == ConsoleKey.M)
                {
                    //Console.WriteLine("[CommandAnalyzer] trying to mute/unmute", e.KeyInfo.Key);
                    if (!EventBus.Event_Bus.Publish("OnMuteUnmute", e, 4))
                    {
                        EventBus.Event_Bus.NotifyEventQueueIsFull("CommandAnalyzer", "OnMuteUnmute");
                    }
                }
                else if (e.KeyInfo.Key == ConsoleKey.W)
                {
                    if (!EventBus.Event_Bus.Publish("OnWeaponChange", e, 3))
                    {
                        EventBus.Event_Bus.NotifyEventQueueIsFull("CommandAnalyzer", "OnWeaponChange");
                    }
                }
                else if (e.KeyInfo.Key == ConsoleKey.Spacebar)
                {
                    if (!EventBus.Event_Bus.Publish("OnWeaponFire", e, 3))
                    {
                        EventBus.Event_Bus.NotifyEventQueueIsFull("CommandAnalyzer", "OnWeaponFire");
                    }
                }
                else if (e.KeyInfo.Key == ConsoleKey.Q)
                {
                    GameOver = true;
                }
                else if (e.KeyInfo.Key == ConsoleKey.S)
                {
                    ShowWeaponTemperature = !ShowWeaponTemperature;
                }
                else
                {
                    Console.WriteLine("\n[CommandAnalyzer] Unknown Command.");
                }
            }

            public void OnLaserData(object sender, GameEventArgs e)
            {
                if (ShowWeaponTemperature)
                {
                    try
                    {
                        int LaserTemperature = (int)TryGetData("LaserTemperature", e); ;
                        Console.WriteLine("[Laser] temperature is: {0}" + new String('.', LaserTemperature), LaserTemperature);
                    }
                    catch
                    {
                        Console.WriteLine("[GameManager] can't retrieve laser state data");
                    }
                }
            }
        }

        public class Vehicle
        {
            //Weapon _weapon = new Gun();
        }
        public class Battery
        {
            private readonly int MaxEnergy = 1000;
            private readonly int MinEnergy = 50;
            public int EnergyLevel = 200;
            private Timer timer;
            public Battery()
            {
                timer = new Timer();
                timer.Interval = 1000;// 1 Sec
                timer.AutoReset = true;
                timer.Elapsed += (sender, eventArgs) => OnTimer();
                timer.Start();
            }
            private void OnTimer()
            {
                GameEventArgs _e = new GameEventArgs();
                _e.CustomParameters = new Hashtable
                {
                    { "BatteryLevel", EnergyLevel },
                    { "BatteryIsOkay", EnergyLevel>MinEnergy }
                };

                if (!EventBus.Event_Bus.Publish("OnBatteryData", _e, 3))
                {
                    EventBus.Event_Bus.NotifyEventQueueIsFull("Battery", "OnBatteryData");
                }
            }
            public void OnLaserFire(object sender, GameEventArgs e)
            {
                try
                {
                    int EnConsumed = (int)TryGetData("EnergyConsumed", e);
                    EnergyLevel -= EnConsumed;
                }
                catch
                {
                    Console.WriteLine("[Battery] can't retrieve laser consumed energy");
                }
            }
        }

        public class Generator
        {

        }

        public class Laser
        {
            private readonly int EnergyConsumption = 10;
            private readonly int MaxTemperature = 70;
            private readonly int MinTemperature = 20;
            private readonly int TempDecrasePerTick = 1;
            private readonly int TempIncrasePerShot = 5;
            private int CurrentTemperature = 20;
            private bool BatteryIsOkay = false;
            private Timer timer;

            public Laser()
            {
                timer = new Timer();
                timer.Interval = 1000;// 1000 mSec
                timer.AutoReset = true;
                timer.Elapsed += (sender, eventArgs) => OnTimer();
                timer.Start();
            }

            private void OnTimer()
            {
                CurrentTemperature -= TempDecrasePerTick;
                if (CurrentTemperature < MinTemperature)
                {
                    CurrentTemperature = MinTemperature;
                }

                GameEventArgs _e = new GameEventArgs();
                _e.CustomParameters = new Hashtable { { "LaserTemperature", CurrentTemperature } };

                if (!EventBus.Event_Bus.Publish("OnLaserData", _e, 3))
                {
                    EventBus.Event_Bus.NotifyEventQueueIsFull("Laser", "OnLaserData");
                }
            }

            public void Fire(object sender, GameEventArgs e)
            {
                string warning = "";
                if (!BatteryIsOkay)
                {
                    warning += "\nLow energy!";
                }
                if (CurrentTemperature + TempIncrasePerShot > MaxTemperature)
                {
                    warning += "\nHigh temperatue!";
                }

                if (warning != "")
                {
                    Console.WriteLine("[Laser] cant fire:" + warning);
                }
                else
                {
                    CurrentTemperature += TempIncrasePerShot;
                    Console.WriteLine("[Laser] firing " + new String('-', CurrentTemperature - MinTemperature) + "(*)");

                    GameEventArgs _e = new GameEventArgs();
                    _e.CustomParameters = new Hashtable { { "EnergyConsumed", EnergyConsumption } };

                    if (!EventBus.Event_Bus.Publish("OnLaserFire", _e, 3))
                    {
                        EventBus.Event_Bus.NotifyEventQueueIsFull("Laser", "OnLaserFire");
                    }
                }
            }

            public void OnBatteryData(object sender, GameEventArgs e)
            {
                try
                {
                    BatteryIsOkay = (bool)TryGetData("BatteryIsOkay", e);
                    int EnergyLevel = (int)TryGetData("BatteryLevel", e); ;
                    Console.WriteLine("[Laser] received battery data:\n" +
                        "battery level = {0}\n" +
                        "battary state is " + (BatteryIsOkay ? "Okay" : "Empty"), EnergyLevel);
                }
                catch
                {
                    Console.WriteLine("[Laser] can't retrieve battery state data");
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
                Console.WriteLine("\n[SOUND] Sound system {0}", (Muted) ? "muted" : "unmuted");
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
                Console.WriteLine("[UI] Pressed key = " + e.KeyInfo.Key.ToString());
                if (!EventBus.Event_Bus.Publish("ReceivedCommand", e))
                {
                    EventBus.Event_Bus.NotifyEventQueueIsFull("UI", "ReceivedCommand");
                }
            }
        }
    }
}