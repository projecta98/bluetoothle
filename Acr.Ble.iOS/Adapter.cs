using System;
using System.Reactive.Linq;
using CoreBluetooth;
using CoreFoundation;
using System.Reactive.Subjects;


namespace Acr.Ble
{
    public class Adapter : IAdapter
    {
        readonly DeviceManager deviceManager;
        readonly CBCentralManager manager;
        readonly Subject<bool> scanStatusChanged;


        public Adapter()
        {
            this.manager = new CBCentralManager(DispatchQueue.DefaultGlobalQueue);
            //this.manager = new CBCentralManager(DispatchQueue.GetGlobalQueue(DispatchQueuePriority.Background));
            this.deviceManager = new DeviceManager(this.manager);
            this.scanStatusChanged = new Subject<bool>();
        }


        public bool IsScanning => this.manager.IsScanning;

        public AdapterStatus Status
        {
            get
            {
                switch (this.manager.State)
                {
                    case CBCentralManagerState.PoweredOff:
                        return AdapterStatus.PoweredOff;

                    case CBCentralManagerState.PoweredOn:
                        return AdapterStatus.PoweredOn;

                    case CBCentralManagerState.Resetting:
                        return AdapterStatus.Resetting;

                    case CBCentralManagerState.Unauthorized:
                        return AdapterStatus.Unauthorized;

                    case CBCentralManagerState.Unsupported:
                        return AdapterStatus.Unsupported;

                    case CBCentralManagerState.Unknown:
                    default:
                        return AdapterStatus.Unknown;
                }
            }
        }


        public IObservable<bool> WhenScanningStatusChanged()
        {
            return this.scanStatusChanged;
        }


        IObservable<IScanResult> scanner;
        public IObservable<IScanResult> Scan()
        {
            this.scanner = this.scanner ?? this.CreateScanner();
            return this.scanner;
        }


        IObservable<IScanResult> bgScanner;
        public IObservable<IScanResult> BackgroundScan(Guid serviceUuid)
        {
            this.bgScanner = this.bgScanner ?? this.CreateScanner(serviceUuid);
            return this.bgScanner;
        }


        public IObservable<AdapterStatus> WhenStatusChanged()
        {
            return Observable.Create<AdapterStatus>(ob =>
            {
                ob.OnNext(this.Status);
                var handler = new EventHandler((sender, args) => ob.OnNext(this.Status));
                this.manager.UpdatedState += handler;

                return () => this.manager.UpdatedState -= handler;
            });
        }


        public IObservable<IDevice> WhenDeviceStatusChanged()
        {
            return Observable.Create<IDevice>(observer =>
            {
                var chandler = new EventHandler<CBPeripheralEventArgs>((sender, args) =>
                {
                    var device = this.deviceManager.GetDevice(args.Peripheral);
                    observer.OnNext(device);
                });
                var dhandler = new EventHandler<CBPeripheralErrorEventArgs>((sender, args) =>
                {
                    var device = this.deviceManager.GetDevice(args.Peripheral);
                    observer.OnNext(device);
                });

                this.manager.ConnectedPeripheral += chandler;
                this.manager.DisconnectedPeripheral += dhandler;

                return () =>
                {
                    this.manager.ConnectedPeripheral -= chandler;
                    this.manager.DisconnectedPeripheral -= dhandler;
                };
            });
        }



        IObservable<IScanResult> CreateScanner(Guid? serviceUuid = null)
        {
            return Observable.Create<IScanResult>(ob =>
            {
                this.deviceManager.Clear();

                var handler = new EventHandler<CBDiscoveredPeripheralEventArgs>((sender, args) =>
                {
                    var device = this.deviceManager.GetDevice(args.Peripheral);
                    ob.OnNext(new ScanResult(
                        device,
                        args.RSSI?.Int32Value ?? 0,
                        new AdvertisementData(args.AdvertisementData))
                    );
                });
                this.manager.DiscoveredPeripheral += handler;
                if (serviceUuid == null)
                {
                    this.manager.ScanForPeripherals(null, new PeripheralScanningOptions { AllowDuplicatesKey = true });
                }
                else
                {
                    var uuid = serviceUuid.Value.ToCBUuid();
                    this.manager.ScanForPeripherals(uuid);
                }

                this.scanStatusChanged.OnNext(true);

                return () =>
                {
                    this.manager.StopScan();
                    this.manager.DiscoveredPeripheral -= handler;
                    this.scanStatusChanged.OnNext(false);
                };
            })
            .Publish()
            .RefCount();
        }
    }
}