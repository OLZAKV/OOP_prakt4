using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Timers;

namespace Механізм_повторних_спроб_із_затримкою_та_рандомізацією
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
                Generator generator = new Generator();

                // 2. register event subscribers
                EventBus.Event_Bus.Subscribe("OnLevelUp", soundManager.OnLevelUp);
                EventBus.Event_Bus.Subscribe("OnMuteUnmute", soundManager.OnMuteUnmute);
                EventBus.Event_Bus.Subscribe("OnLevelUp", uiManager.ShowLevelUpBaner);
                EventBus.Event_Bus.Subscribe("OnLevelUp", uiManager.ShowCurrentLevelLabel);
                EventBus.Event_Bus.Subscribe("KeyPressed", uiManager.LongRun_KeyPress);
                EventBus.Event_Bus.Subscribe("ReceivedCommand", gameManager.CommandAnalyzer);
                EventBus.Event_Bus.Subscribe("OnLaserData", gameManager.OnLaserData);

                EventBus.Event_Bus.Subscribe("OnLaserFire", battery.OnLaserFire);
                EventBus.Event_Bus.Subscribe("OnGeneratorData", battery.OnGeneratorData);

                EventBus.Event_Bus.Subscribe("OnWeaponFire", laser.Fire);
                EventBus.Event_Bus.Subscribe("OnBatteryData", laser.OnBatteryData);
                
                EventBus.Event_Bus.Subscribe("OnRefuel", generator.OnRefuel);
                EventBus.Event_Bus.Subscribe("OnStartEngine", generator.OnStartEngine);


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

                    if(!EventBus.Event_Bus.Publish("KeyPressed", e, 4))
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
            public ConsoleKeyInfo KeyInfo { get; set; }
            public bool Repeated { get; set; } = false;
            public Hashtable CustomParameters { get; set; }
       }

        public class EventBus
        {
            public readonly int BusFrequencyPeriod = 50;// mSec
            public readonly int MaxDelationTime = 60 * 60 * 1000;// 1 hour
            private int MaxQueueLength = 5;
            private static int MaxPriorityLevels = 5;

            public static readonly EventBus Event_Bus = new EventBus();
            readonly Dictionary<String, EventBox> subscriptions = new Dictionary<String, EventBox>();
            public Dictionary<string, Timer> delayedHandlersTimers = new Dictionary<string, Timer>();

            //an array of multithreading safe FIFO queues
            readonly ConcurrentQueue<EventQueueElement>[] cqArray = new ConcurrentQueue<EventQueueElement>[MaxPriorityLevels];
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
                //extracting one event per tick from event queues starting from higher priority queue first
                foreach (ConcurrentQueue<EventQueueElement>  cq in cqArray)
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
                if (!subscriptions.TryGetValue(EvName, out EventBox forEvBox))
                {
                    forEvBox = new EventBox();
                    subscriptions.Add(EvName, forEvBox);
                }

                forEvBox.Event += d;
            }
            public void UnSubscribe(string EvName, EventHandler<GameEventArgs> d)
            {
                if (!subscriptions.TryGetValue(EvName, out EventBox forEvBox))
                {
                    subscriptions.Remove(EvName);
                }
                else
                {
                    forEvBox.Event -= d;
                }
            }
            public bool Publish(string EvName, GameEventArgs e, int Priority  = 0)
            {
                if (!subscriptions.TryGetValue(EvName, out EventBox forEvBox))
                {
                    forEvBox = new EventBox();
                    subscriptions.Add(EvName, forEvBox);
                }

                if (cqArray[Priority].Count < MaxQueueLength)
                {
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
                Console.WriteLine("\n[{0}] can't enqueue event '{1}'\n",sender,eventName);
            }

            public Timer GetRecurrentEventTimer(string delayedEventKey, GameEventArgs e, out bool newtimer)
            {

                int DelationTime;
                if (!EventBus.Event_Bus.delayedHandlersTimers.TryGetValue(delayedEventKey, out Timer _timer))
                {
                    DelationTime = (int)EventBus.Event_Bus.BusFrequencyPeriod * 4;
                    _timer = new Timer
                    {
                        AutoReset = false,
                        Interval = DelationTime
                    };
                    newtimer = true;
                    EventBus.Event_Bus.delayedHandlersTimers.Add(delayedEventKey, _timer);
                }
                else
                {
                    DelationTime = (int)_timer.Interval;
                    newtimer = false;
                }

                if (e.Repeated)
                {
                    //double delation time with random 10% addition
                    DelationTime += (int)DelationTime + new Random().Next(DelationTime / 10);
                }
                else
                {
                    //if its first loop we will reduce stored delation time
                    DelationTime = (int)DelationTime / 2;
                    e.Repeated = true;
                }

                //saving new delation time
                _timer.Interval = DelationTime;
                EventBus.Event_Bus.delayedHandlersTimers[delayedEventKey] = _timer;

                return _timer; 
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
                 }
                else
                {
                    ////Console.WriteLine("[EventBox] didn't find any event handler");
                    //Don't worry, it seems nobody is subscribed for it
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

                e.CustomParameters = new Hashtable
                {
                    { "GameLevel", level }
                };

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
                    Console.WriteLine("G - start/stop generator");
                    Console.WriteLine("F - refuel generator");
                    Console.WriteLine("H - help (this page)");
                    Console.WriteLine("L - Level up command");
                    Console.WriteLine("M - mute/unmute sound");
                    Console.WriteLine("S - show/hide laser data");
                    //Console.WriteLine("W - select weapon: gun/canon/laser");
                    Console.WriteLine("spacebar - fire");
                    Console.WriteLine("Q - exit the game");
                }
                else if (e.KeyInfo.Key == ConsoleKey.G)
                {
                    if (!EventBus.Event_Bus.Publish("OnStartEngine", e, 3))
                    {
                        EventBus.Event_Bus.NotifyEventQueueIsFull("CommandAnalyzer", "OnStartEngine");
                    }
                }
                else if (e.KeyInfo.Key == ConsoleKey.F)
                {
                    if (!EventBus.Event_Bus.Publish("OnRefuel", e, 3))
                    {
                        EventBus.Event_Bus.NotifyEventQueueIsFull("CommandAnalyzer", "OnRefuel");
                    }
                }
                else if (e.KeyInfo.Key == ConsoleKey.L)
                {
                    LevelUp();
                }
                else if (e.KeyInfo.Key == ConsoleKey.M)
                {
                    if (!EventBus.Event_Bus.Publish("OnMuteUnmute", e, 4))
                    {
                        EventBus.Event_Bus.NotifyEventQueueIsFull("CommandAnalyzer", "OnMuteUnmute");
                    }
                }
                //else if (e.KeyInfo.Key == ConsoleKey.W)
                //{
                //    if (!EventBus.Event_Bus.Publish("OnWeaponChange", e, 3))
                //    {
                //        EventBus.Event_Bus.NotifyEventQueueIsFull("CommandAnalyzer", "OnWeaponChange");
                //    }
                //}
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
                        Console.WriteLine("[Laser] temperature is: {0}" + new String('.', LaserTemperature/2), LaserTemperature);
                    }
                    catch
                    {
                        Console.WriteLine("[Laser] can't retrieve battery state data");
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
                timer.Interval = 5000;// 1 Sec
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

                //priority level is highest
                if (!EventBus.Event_Bus.Publish("OnBatteryData", _e, 0))
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
            public void OnGeneratorData(object sender, GameEventArgs e)
            {
                try
                {
                    int ProducedEnergy = (int)TryGetData("ProducedEnergy", e);
                    if (EnergyLevel + ProducedEnergy< MaxEnergy)
                    {
                        EnergyLevel += ProducedEnergy;
                    }
                    Console.WriteLine("[Battery] /\\/\\/\\/\\/\\/\\/\\/\\/\\/\\/\\/\\ charging");
                }
                catch
                {
                    Console.WriteLine("[Laser] can't retrieve battery state data");
                }
            }

        }

        public class Generator
        {
            bool GeneratorIsWorking = false;
            private readonly int ProdusedEnergyPerTick = 10;
            private readonly int ConsumedFuelPerTick = 2;
            private readonly int MaxFuelCapacity = 20;//200;
            public int CurrentFuelLevel = 10;

            private Timer timer;
            public Generator()
            {
                timer = new Timer();
                timer.Interval = 500;// mSec
                timer.AutoReset = true;
                timer.Elapsed += (sender, eventArgs) => OnTimer();
            }
            private void OnTimer()
            {
                if (CurrentFuelLevel > ConsumedFuelPerTick)
                {
                    CurrentFuelLevel -= ConsumedFuelPerTick;
                    
                    GameEventArgs _e = new GameEventArgs();
                    _e.CustomParameters = new Hashtable
                    {
                        { "ProducedEnergy", ProdusedEnergyPerTick }
                    };

                    Console.WriteLine("[Generator] CurrentFuelLevel = {0}", CurrentFuelLevel);
                    if (!EventBus.Event_Bus.Publish("OnGeneratorData", _e, 3))
                    {
                        EventBus.Event_Bus.NotifyEventQueueIsFull("Generator", "OnGeneratorData");
                    }
                }
                else
                {
                    timer.Stop();
                    GeneratorIsWorking = false;

                    GameEventArgs _e = new GameEventArgs();
                    if (!EventBus.Event_Bus.Publish("OnGeneratorOutOfFuel", _e, 3))
                    {
                        EventBus.Event_Bus.NotifyEventQueueIsFull("Generator", "OnGeneratorOutOfFuel");
                    }
                }
            }

            public void OnStartEngine(object _sender, GameEventArgs e)
            {
                if (GeneratorIsWorking)
                {
                    if (!e.Repeated)
                    {
                        timer.Stop();
                        GeneratorIsWorking = false;
                    }
                }
                else
                {
                    if (CurrentFuelLevel > ConsumedFuelPerTick)
                    {
                        timer.Start();
                        GeneratorIsWorking = true;
                    }
                    else
                    {
                        //start reccuring event
                        Timer _timer = EventBus.Event_Bus.GetRecurrentEventTimer("Generator_OnStartEngine", e, out bool newtimer);
                        Console.WriteLine("[Generator]              trying to start after {0} mSecs", _timer.Interval);

                        if (newtimer)
                        {
                            _timer.Elapsed += (sender, eventArgs) => this.OnStartEngine(this, e);
                        }
                        _timer.Start();
                    }
                }
            }

            public void OnRefuel(object _sender, GameEventArgs e)
            {
                CurrentFuelLevel = MaxFuelCapacity;
                Console.WriteLine("            [Generator]         refueled ");
            }
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

                if (!EventBus.Event_Bus.Publish("OnLaserData", _e, 1))
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

                if(warning != "")
                {
                    Console.WriteLine("[Laser] cant fire:"+warning);
                }
                else
                {
                    CurrentTemperature += TempIncrasePerShot;
                    Console.WriteLine("[Laser] firing "+ new String('-', CurrentTemperature - MinTemperature) + "(*)");
 
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
                    Console.WriteLine("[Laser] received battery data:\n"+
                        "battery level = {0}\n"+
                        "battary state is " + (BatteryIsOkay ? "Okay":"Empty"), EnergyLevel);
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
                try
                {
                    int Level = (int)TryGetData("GameLevel", e);
                    Console.WriteLine("[UI] current level: {0}", Level);
                }
                catch
                {
                    Console.WriteLine("[UI] can't retrieve current level");
                }
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