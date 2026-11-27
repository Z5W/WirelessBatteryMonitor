using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WirelessBatteryMoniter.Models;
using WirelessBatteryMoniter.Services;
using WirelessBatteryMoniter.Services.Hid;
using WirelessBatteryMoniter.UI;

namespace WirelessBatteryMoniter
{
    internal static class SelfTest
    {
        private static DeviceBatteryInfo Reading(string id, int level, bool connected = true, bool stale = false)
        {
            return new DeviceBatteryInfo(id, "Same model", "2.4G", "046D:C54D", level,
                connected, stale, new DateTime(2026, 1, 1));
        }

        private static async Task RunChannelChecks(Action<string,bool> check)
        {
            var blockedReceiver=new TaskCompletionSource<bool>();
            int blockedCalls=0,healthyCalls=0;
            var attached=new List<HidDeviceInfo> {
                new HidDeviceInfo {Path="parallel-blocked",VendorId=0x3151,ProductId=0x5045,UsagePage=0xFFFF,Usage=2,FeatureReportLength=65},
                new HidDeviceInfo {Path="parallel-healthy",VendorId=0x3151,ProductId=0x5045,UsagePage=0xFFFF,Usage=2,FeatureReportLength=65}
            };
            var available=attached;
            using(var receiverScheduler=new HidReceiverScheduler(()=>available,group=>{
                if(group[0].Path=="parallel-blocked") {Interlocked.Increment(ref blockedCalls);blockedReceiver.Task.GetAwaiter().GetResult();return new List<DeviceBatteryInfo>{Reading("blocked",10)};}
                Interlocked.Increment(ref healthyCalls);return new List<DeviceBatteryInfo>{Reading("healthy",70)};
            },timeoutMs:100))
            {
                try
                {
                    var first=await receiverScheduler.PollAsync(10,false,CancellationToken.None,false,40);
                    check("blocked HID receiver does not prevent healthy publication",first.Devices.Any(d=>d.Id=="healthy" && !d.IsStale));
                    await Task.Delay(120);
                    var next=await receiverScheduler.PollAsync(10,true,CancellationToken.None,false,40);
                    check("timed out receiver remains single flight while healthy receiver refreshes",blockedCalls==1 && healthyCalls>=2 && next.Devices.Any(d=>d.Id=="healthy" && !d.IsStale) && next.Error.Contains("超时"));
                    available=new List<HidDeviceInfo>{attached[1]};
                    await receiverScheduler.PollAsync(10,true,CancellationToken.None,false,40);
                    available=attached;
                    await receiverScheduler.PollAsync(10,true,CancellationToken.None,false,40);
                    check("receiver reinsert does not overlap native cleanup",blockedCalls==1);
                }
                finally { blockedReceiver.TrySetResult(true); }
            }
            var completedBeforeTimeout=new TaskCompletionSource<IReadOnlyList<DeviceBatteryInfo>>();
            using(var delayedCollection=new BatteryChannel(token=>completedBeforeTimeout.Task,()=>null,()=>{},timeoutMs:200))
            {
                await delayedCollection.PollAsync(10,false,CancellationToken.None,1);
                completedBeforeTimeout.SetResult(new[]{Reading("already-complete",70)});
                await Task.Delay(300);
                var result=await delayedCollection.PollAsync(10,false,CancellationToken.None,1);
                check("late collection preserves query completed before timeout",result.Error==null && result.Devices.Count==1 && !result.Devices[0].IsStale);
            }
            DateTime retryNow=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);
            var schedule=new DeviceRetrySchedule(()=>retryNow);
            schedule.Record("receiver|slot:1",false,10);
            check("slot failure leaves other slots eligible",!schedule.CanRead("RECEIVER|slot:1",false) && schedule.CanRead("receiver|slot:2",false));
            check("forced refresh bypasses slot backoff",schedule.CanRead("receiver|slot:1",true));
            retryNow=retryNow.AddSeconds(20);
            check("slot retry becomes due",schedule.CanRead("receiver|slot:1",false));
            schedule.Record("receiver|slot:1",false,10);
            retryNow=retryNow.AddSeconds(21);
            check("repeated failure increases per-slot delay",!schedule.CanRead("receiver|slot:1",false));
            schedule.Record("receiver|slot:1",true,10);
            check("slot recovery resets backoff",schedule.CanRead("receiver|slot:1",false));
            schedule.Record("removed",false,10);schedule.Retain(new[]{"receiver|slot:1"});
            check("unplugged receiver loses old retry delay",schedule.CanRead("removed",false));
            int badReads=0,goodReads=0;bool failReceiver=true;
            var receivers=new List<HidDeviceInfo> {
                new HidDeviceInfo {Path="bad-receiver",VendorId=0x3151,ProductId=0x5045,UsagePage=0xFFFF,Usage=2,FeatureReportLength=65},
                new HidDeviceInfo {Path="good-receiver",VendorId=0x3151,ProductId=0x5045,UsagePage=0xFFFF,Usage=2,FeatureReportLength=65}
            };
            using(var provider=new HidBatteryProvider(()=>receivers,group=>{
                if(group[0].Path=="bad-receiver") {badReads++;return failReceiver ? new List<DeviceBatteryInfo>() : new List<DeviceBatteryInfo>{Reading("bad",45)};}
                goodReads++;return new List<DeviceBatteryInfo>{Reading("good",65)};
            },()=>retryNow))
            using(var channel=new BatteryChannel(provider.GetDevicesAsync,()=>provider.LastWarning,()=>{},()=>retryNow,backoffOnWarning:false))
            {
                await channel.PollAsync(10,false,CancellationToken.None,1000);
                retryNow=retryNow.AddSeconds(10);
                var healthy=await channel.PollAsync(10,false,CancellationToken.None,1000);
                check("receiver warning does not back off healthy HID channel",badReads==1 && goodReads==2 && healthy.Devices.Count==1);
                provider.RequestRefresh();failReceiver=false;
                var recovered=await channel.PollAsync(10,true,CancellationToken.None,1000);
                check("manual receiver refresh bypasses delay and recovers",badReads==2 && recovered.Devices.Count==2 && recovered.Error==null);
                retryNow=retryNow.AddSeconds(10);failReceiver=true;
                await channel.PollAsync(10,false,CancellationToken.None,1000);
                retryNow=retryNow.AddSeconds(10);
                var cached=await channel.PollAsync(10,false,CancellationToken.None,1000);
                check("backoff keeps old reading unknown without delaying healthy receiver",cached.Devices.Any(d=>d.Id=="bad" && d.IsConnectionUnknown && d.IsStale) && goodReads==5 && badReads==3);
            }
            var deferred=new TaskCompletionSource<IReadOnlyList<DeviceBatteryInfo>>();
            DateTime completed=new DateTime(2026,1,1,12,0,0,DateTimeKind.Utc);
            using(var channel=new BatteryChannel(token=>deferred.Task,()=>null,()=>{},()=>completed))
            {
                var querying=await channel.PollAsync(10,false,CancellationToken.None,1);
                check("pending query is not a failure or completed scan",querying.IsScanning && querying.Error==null && querying.Timestamp==DateTime.MinValue);
                var filtered=new AppSettings().DisplaySnapshot(querying);
                check("display filtering preserves pending scan state",filtered.IsScanning && filtered.Timestamp==DateTime.MinValue);
                deferred.SetResult(new[]{Reading("delayed",55)});
                var result=await channel.PollAsync(10,false,CancellationToken.None,200);
                check("completed query publishes result and completion time",!result.IsScanning && result.Error==null && result.Timestamp==completed.ToLocalTime());
                completed=completed.AddSeconds(2);
                var idle=await channel.PollAsync(10,false,CancellationToken.None,1);
                check("idle collection does not advance scan timestamp",idle.Timestamp==result.Timestamp && !idle.IsScanning);
            }
            using(var monitor=new BatteryMonitorService(
                new BatteryChannel(async token=>{await Task.Delay(1800,token);return new[]{Reading("slow-diagnostic",55)};},()=>null,()=>{}),
                new BatteryChannel(token=>Task.FromResult<IReadOnlyList<DeviceBatteryInfo>>(new[]{Reading("quick-diagnostic",65)}),()=>null,()=>{})))
            {
                var result=await monitor.ScanAsync(CancellationToken.None,true,true);
                check("diagnostic waits beyond UI collection budget for both providers",!result.IsScanning && result.Error==null && result.Devices.Count==2);
            }
            var stuck=new TaskCompletionSource<IReadOnlyList<DeviceBatteryInfo>>();
            using(var monitor=new BatteryMonitorService(
                new BatteryChannel(token=>stuck.Task,()=>null,()=>{},timeoutMs:30),
                new BatteryChannel(token=>Task.FromResult<IReadOnlyList<DeviceBatteryInfo>>(new[]{Reading("surviving",65)}),()=>null,()=>{})))
            {
                var result=await monitor.ScanAsync(CancellationToken.None,true,true);
                check("diagnostic timeout preserves healthy channel and reports failure",result.Error!=null && result.Error.Contains("超时") && result.Devices.Count==1);
                stuck.SetResult(new DeviceBatteryInfo[0]);
            }
            DateTime now=new DateTime(2026,1,1);int calls=0,disposed=0;
            var blocked=new TaskCompletionSource<IReadOnlyList<DeviceBatteryInfo>>();
            var release=new TaskCompletionSource<bool>();
            var slow=new BatteryChannel(token=>{Interlocked.Increment(ref calls);return blocked.Task;},()=>null,()=>{Interlocked.Increment(ref disposed);release.TrySetResult(true);},()=>now,40);
            var quick=new BatteryChannel(token=>Task.FromResult<IReadOnlyList<DeviceBatteryInfo>>(new[]{Reading("fast",60)}),()=>null,()=>{},()=>now);
            var fast=await quick.PollAsync(10,false,CancellationToken.None,200);
            await slow.PollAsync(10,false,CancellationToken.None,10);
            await Task.Delay(60);
            var pending=await slow.PollAsync(10,true,CancellationToken.None,1);
            check("slow native cleanup does not block healthy channel",fast.Devices.Count==1 && pending.Error.Contains("超时"));
            check("manual refresh never overlaps an active provider",calls==1);
            slow.Dispose();await Task.Delay(10);check("provider not disposed before native completion",disposed==0);
            blocked.SetResult(new[]{Reading("late",10)});
            check("provider disposed after late completion",await Task.WhenAny(release.Task,Task.Delay(2000))==release.Task);
            quick.Dispose();
            int attempts=0;
            using(var retry=new BatteryChannel(token=>{
                attempts++;
                if(attempts==1)throw new InvalidOperationException("test failure");
                return Task.FromResult<IReadOnlyList<DeviceBatteryInfo>>(new[]{Reading("recovered",40)});
            },()=>null,()=>{},()=>now))
            {
                var failure=await retry.PollAsync(10,false,CancellationToken.None,200);
                await retry.PollAsync(10,false,CancellationToken.None,1);
                check("failed channel backs off before next query",attempts==1 && failure.Error.Contains("失败"));
                var recovered=await retry.PollAsync(10,true,CancellationToken.None,200);
                check("manual refresh bypasses backoff and clears failure",attempts==2 && recovered.Error==null && recovered.Devices[0].Level==40);
                now=now.AddSeconds(9);await retry.PollAsync(10,false,CancellationToken.None,1);
                check("successful channel respects polling interval",attempts==2);
                now=now.AddSeconds(2);await retry.PollAsync(10,false,CancellationToken.None,200);
                check("successful channel resumes normal interval",attempts==3);
            }
            check("exponential retry bounded at five minutes",BatteryChannel.RetrySeconds(1,10)==20 && BatteryChannel.RetrySeconds(2,10)==40 && BatteryChannel.RetrySeconds(20,120)==300);
            using(var canceled=new CancellationTokenSource())
            using(var channel=new BatteryChannel(token=>Task.FromResult<IReadOnlyList<DeviceBatteryInfo>>(new[]{Reading("x",1)}),()=>null,()=>{}))
            {
                canceled.Cancel();bool threw=false;
                try{await channel.PollAsync(10,false,canceled.Token,1);}catch(OperationCanceledException){threw=true;}
                check("shutdown prevents channel publication",threw);
            }
        }

        private static void RunHistoryChecks(Action<string,bool> check)
        {
            string directory=Path.Combine(Path.GetTempPath(),"battery-history-test-"+Guid.NewGuid().ToString("N"));
            DateTime now=DateTime.UtcNow;
            var history=new BatteryHistoryService(directory,()=>now);
            Func<int,bool,BatteryReadingSource,DeviceBatteryInfo> reading=(level,stale,source)=>new DeviceBatteryInfo("history","History","HID","",level,true,stale,now,source);
            Action<DeviceBatteryInfo> record=d=>history.Record(new BatterySnapshot(new[]{d},now,null,false));
            var first=reading(50,false,BatteryReadingSource.DeviceReported);record(first);record(first);
            check("history deduplicates same observed reading",history.Read().Count==1);
            now=now.AddMinutes(1);record(reading(50,false,BatteryReadingSource.DeviceReported));
            check("history unchanged value waits for heartbeat",history.Read().Count==1);
            record(reading(40,true,BatteryReadingSource.DeviceReported));
            check("history excludes cache",history.Read().Count==1);
            record(reading(50,false,BatteryReadingSource.VoltageEstimate));
            check("history retains source and breaks uncertain interval",history.Read().Count==2 && history.Read()[1].LevelText=="约 50%" && history.Read()[0].Session!=history.Read()[1].Session);
            now=now.AddMinutes(10);record(reading(50,false,BatteryReadingSource.VoltageEstimate));
            check("history records ten minute heartbeat",history.Read().Count==3);
            history.Record(new BatterySnapshot(new[]{reading(1,false,BatteryReadingSource.Demo)},now,null,true));
            check("history excludes demo snapshots",history.Read().Count==3);
            history.RetrySave();var reloaded=new BatteryHistoryService(directory,()=>now);
            check("history survives restart with UTC timestamps",reloaded.Read().Count==3 && Math.Abs((reloaded.Read()[0].Utc-first.LastSeen.ToUniversalTime()).TotalMilliseconds)<1 && reloaded.Read()[0].Utc.Kind==DateTimeKind.Utc);
            now=now.AddDays(8);record(reading(60,false,BatteryReadingSource.DeviceReported));history.RetrySave();
            check("history prunes beyond seven days",history.Read().Count==1 && new BatteryHistoryService(directory,()=>now).Read().Count==1);
            string corrupt=Path.Combine(directory,"corrupt");Directory.CreateDirectory(corrupt);File.WriteAllText(Path.Combine(corrupt,"battery-history.json"),"invalid");
            var broken=new BatteryHistoryService(corrupt,()=>now);broken.Record(new BatterySnapshot(new[]{reading(60,false,BatteryReadingSource.DeviceReported)},now,null,false));broken.RetrySave();
            check("corrupt history is visible and never overwritten",broken.Error!=null && broken.Read().Count==0 && File.ReadAllText(Path.Combine(corrupt,"battery-history.json"))=="invalid");
            string blocked=Path.Combine(directory,"blocked");File.WriteAllText(blocked,"file blocks directory");
            var failed=new BatteryHistoryService(blocked,()=>now);failed.Record(new BatterySnapshot(new[]{reading(60,false,BatteryReadingSource.DeviceReported)},now,null,false));
            check("history save failure retains pending points",failed.Error!=null && failed.Read().Count==1);
            File.Delete(blocked);failed.RetrySave();
            check("history save retry persists retained points",failed.Error==null && new BatteryHistoryService(blocked,()=>now).Read().Count==1);
        }

        private static void RunRegressionChecks(Action<string, bool> check)
        {
            int adcLevel; BatteryChargeState adcCharge;
            check("ADC valid voltage and charging flags",HidBatteryProvider.TryParseAdcBattery(new byte[]{0x0F,0xA0,3},out adcLevel,out adcCharge) && adcLevel>0 && adcCharge==BatteryChargeState.Charging);
            check("ADC unavailable report rejected",!HidBatteryProvider.TryParseAdcBattery(new byte[]{0x0F,0xA0,2},out adcLevel,out adcCharge));
            check("ADC short report rejected",!HidBatteryProvider.TryParseAdcBattery(new byte[]{0x0F},out adcLevel,out adcCharge));
            check("ADC implausible voltage rejected",!HidBatteryProvider.TryParseAdcBattery(new byte[]{0,0,1},out adcLevel,out adcCharge));
            check("voltage charging status",HidBatteryProvider.ParseVoltageChargeState(new byte[]{15,160,128})==BatteryChargeState.Charging);
            check("voltage charged status",HidBatteryProvider.ParseVoltageChargeState(new byte[]{15,160,129})==BatteryChargeState.Charged);
            check("voltage not charging status",HidBatteryProvider.ParseVoltageChargeState(new byte[]{15,160,130})==BatteryChargeState.NotCharging);
            check("voltage reserved status unknown",HidBatteryProvider.ParseVoltageChargeState(new byte[]{15,160,131})==BatteryChargeState.Unknown);
            check("voltage missing flags unknown",HidBatteryProvider.ParseVoltageChargeState(new byte[]{15,160})==BatteryChargeState.Unknown);
            RunHistoryChecks(check);
            RunChannelChecks(check).GetAwaiter().GetResult();
            var thresholds=new AppSettings {LowBatteryThreshold=20};
            thresholds.Devices.Add(new DevicePreference {Id="higher",LowBatteryThreshold=40});
            thresholds.Devices.Add(new DevicePreference {Id="lower",LowBatteryThreshold=5});
            var mixed=new BatterySnapshot(new[]{Reading("higher",30),Reading("lower",8)},DateTime.Now,null,false);
            check("tray considers each threshold rather than minimum level",TrayIconRenderer.StatusColor(mixed,thresholds)==Theme.Amber);
            check("level above custom five percent threshold is not red",Theme.BatteryColor(8,5)==Theme.Green);
            thresholds.Devices[0].LowBatteryThreshold=25;
            check("changing threshold immediately clears tray warning",TrayIconRenderer.StatusColor(mixed,thresholds)==Theme.Green);
            thresholds.Devices[0].LowBatteryThreshold=40;thresholds.Devices[0].Hidden=true;
            check("hidden device does not change tray warning",TrayIconRenderer.StatusColor(mixed,thresholds)==Theme.Green);
            thresholds.Devices[0].Hidden=false;
            var charging=new DeviceBatteryInfo("higher","Charging","2.4G","",4,true,false,DateTime.Now,BatteryReadingSource.DeviceReported,BatteryChargeState.Charging);
            var chargingTraySnapshot=new BatterySnapshot(new[]{charging,Reading("lower",8)},DateTime.Now,null,false);
            check("charging device does not trigger tray warning",TrayIconRenderer.StatusColor(chargingTraySnapshot,thresholds)==Theme.Green && !charging.IsLowBattery(40));
            check("cached low battery does not trigger tray warning",TrayIconRenderer.StatusColor(new BatterySnapshot(new[]{Reading("higher",4,true,true),Reading("lower",8)},DateTime.Now,null,false),thresholds)==Theme.Green);
            var perDevice=new AppSettings();
            perDevice.Devices.Add(new DevicePreference {Id="one",Alias="办公鼠标",LowBatteryThreshold=30});
            perDevice.Devices.Add(new DevicePreference {Id="two",Hidden=true,LowBatteryThreshold=5});
            perDevice.Devices.Add(new DevicePreference {Id="off",DisableNotifications=true});
            perDevice.Normalize();
            var clone=perDevice.Clone();clone.Devices[0].Alias="Changed";
            check("device preferences deep clone",perDevice.Devices[0].Alias=="办公鼠标");
            using(var stream=new MemoryStream())
            {
                var serializer=new DataContractJsonSerializer(typeof(AppSettings));serializer.WriteObject(stream,perDevice);stream.Position=0;
                var loaded=(AppSettings)serializer.ReadObject(stream);loaded.Normalize();
                check("device preferences persist thresholds aliases and visibility",loaded.ThresholdFor("ONE")==30 && loaded.IsHidden("two") && loaded.PreferenceFor("one").Alias=="办公鼠标" && loaded.PreferenceFor("off").DisableNotifications);
            }
            var preferenceSnapshot=new BatterySnapshot(new[]{Reading("one",25),Reading("two",1),Reading("off",1)},DateTime.Now,null,false);
            var display=perDevice.DisplaySnapshot(preferenceSnapshot);
            check("hidden device excluded from displayed statistics",display.Devices.Count==2 && display.Devices.All(d=>d.Id!="two"));
            var alias=display.Devices.Single(d=>d.Id=="one");
            check("alias preserves stable ID and original name",alias.Name=="办公鼠标" && alias.OriginalName=="Same model" && alias.Id=="one");
            int customAlerts=0;string alertName=null;int alertThreshold=0;
            var customNotifier=new NotificationService(null,perDevice);perDevice.NotificationSound=false;
            customNotifier.ToastPresenter=(devices,threshold)=>{customAlerts+=devices.Count;alertName=devices[0].Name;alertThreshold=threshold;};
            customNotifier.Process(preferenceSnapshot);
            check("per-device threshold and alias applied while hidden disabled devices skipped",customAlerts==1 && alertName=="办公鼠标" && alertThreshold==30);
            perDevice.PreferenceFor("two").Hidden=false;customNotifier.Process(preferenceSnapshot);
            check("unhidden device can alert independently",customAlerts==2 && alertThreshold==5);
            var asleep=new DeviceBatteryInfo("sleep", "Sleeping", "2.4G", "", 5,true,true,DateTime.Now,BatteryReadingSource.DeviceReported,BatteryChargeState.Unknown,DeviceActivityState.Asleep);
            var sleepSnapshot=new BatterySnapshot(new[]{asleep},DateTime.Now,null,false);
            check("explicit sleep has distinct text and no current battery statistic",asleep.StateText.Contains("休眠") && !sleepSnapshot.MinimumLevel.HasValue && sleepSnapshot.ConnectedCount==0);
            customNotifier.Process(sleepSnapshot);check("sleeping device does not alert",customAlerts==2);
            check("missing query does not infer sleep",!asleep.AsUnconfirmedCache().IsSleeping && !Reading("awake",50).AsUnconfirmedCache().IsSleeping);
            foreach (byte chargeCode in new byte[] { 1, 2, 4 })
                check("HID++ charging status " + chargeCode, HidBatteryProvider.ParseLogitechChargeState(new byte[] { 10, 1, chargeCode }) == BatteryChargeState.Charging);
            check("HID++ charged state", HidBatteryProvider.ParseLogitechChargeState(new byte[] { 10, 1, 3 }) == BatteryChargeState.Charged);
            check("HID++ discharging state", HidBatteryProvider.ParseLogitechChargeState(new byte[] { 10, 1, 0 }) == BatteryChargeState.NotCharging);
            foreach (byte chargeCode in new byte[] { 5, 6, 7, 255 })
                check("HID++ error or reserved status remains unknown " + chargeCode, HidBatteryProvider.ParseLogitechChargeState(new byte[] { 10, 1, chargeCode }) == BatteryChargeState.Unknown);
            check("missing charging status remains unknown", HidBatteryProvider.ParseLogitechChargeState(null) == BatteryChargeState.Unknown && HidBatteryProvider.ParseLogitechChargeState(new byte[] { 10, 1 }) == BatteryChargeState.Unknown);
            int chargeAlerts = 0;
            var chargeNotifier = new NotificationService(null, new AppSettings { NotificationsEnabled=true, NotificationSound=false, LowBatteryThreshold=20 });
            chargeNotifier.ToastPresenter = (devices, threshold) => chargeAlerts += devices.Count;
            Func<BatteryChargeState, bool, DeviceBatteryInfo> chargeReading = (state, stale) => new DeviceBatteryInfo("charging-test", "Mouse", "2.4G", "", 10, true, stale, new DateTime(2026,1,1), BatteryReadingSource.DeviceReported, state);
            Action<BatteryChargeState, bool> chargeFeed = (state, stale) => chargeNotifier.Process(new BatterySnapshot(new[] { chargeReading(state, stale) }, DateTime.Now, null, false));
            chargeFeed(BatteryChargeState.Charging, false);
            chargeFeed(BatteryChargeState.Charged, false);
            check("charging and charged low readings never alert", chargeAlerts == 0);
            chargeFeed(BatteryChargeState.NotCharging, false);
            chargeFeed(BatteryChargeState.NotCharging, false);
            check("unplugging at low level alerts once", chargeAlerts == 1);
            chargeFeed(BatteryChargeState.Charging, true);
            chargeFeed(BatteryChargeState.NotCharging, false);
            check("cached charging state does not rearm alert", chargeAlerts == 1);
            chargeFeed(BatteryChargeState.Charging, false);
            chargeFeed(BatteryChargeState.NotCharging, false);
            check("confirmed charging rearms next low battery episode", chargeAlerts == 2);
            chargeFeed(BatteryChargeState.Unknown, false);
            check("unknown charging status does not rearm alert", chargeAlerts == 2);
            chargeNotifier.Reset();
            chargeFeed(BatteryChargeState.Unknown, false);
            check("unsupported charging status retains low battery alerts", chargeAlerts == 3);
            var chargeCache = chargeReading(BatteryChargeState.Charging, false).AsUnconfirmedCache();
            check("charging cache preserves history but not current charging claim", chargeCache.ChargeState == BatteryChargeState.Charging && !chargeCache.IsChargingOrCharged && chargeCache.IsStale);
            var chargingSnapshot = new BatterySnapshot(new[] { chargeReading(BatteryChargeState.Charging, false) }, DateTime.Now, null, false);
            check("failure preserves historical charge state", chargingSnapshot.WithFailure("test").Devices[0].ChargeState == BatteryChargeState.Charging && !chargingSnapshot.WithFailure("test").Devices[0].IsChargingOrCharged);
            check("tray shows confirmed charging", TrayTextFormatter.BuildFull(chargingSnapshot, false).Contains("正在充电"));
            check("tray does not claim cached charging is current", !TrayTextFormatter.BuildFull(chargingSnapshot.WithFailure("test"), false).Contains("正在充电"));
            chargeNotifier.Reset();
            chargeNotifier.Process(new BatterySnapshot(new[] { chargeReading(BatteryChargeState.Charging, false), Reading("other-low", 9) }, DateTime.Now, null, false));
            check("charging device does not suppress other devices alerts", chargeAlerts == 4);
            var estimate = new DeviceBatteryInfo("estimate", "Estimated mouse", "2.4G", "", 50, true, false,
                new DateTime(2026, 1, 1), BatteryReadingSource.VoltageEstimate);
            check("voltage estimate displays approximate percentage", estimate.LevelText == "约 50%" && estimate.ReadingSourceText == "电压曲线换算");
            var coarse = new DeviceBatteryInfo("coarse", "Coarse mouse", "2.4G", "", 5, true, false,
                estimate.LastSeen, BatteryReadingSource.CoarseLevel);
            check("coarse reading displays approximate percentage", coarse.LevelText == "约 5%" && coarse.ReadingPrecisionText.Contains("粗略档位"));
            var info = new HidDeviceInfo { Path = "sample" };
            var reported = HidBatteryProvider.MakeDevice(info, "reported", 50, "mouse");
            check("HID direct percent not presented as estimate", reported.LevelText == "50%" && reported.ReadingSource == BatteryReadingSource.DeviceReported);
            check("unknown source does not claim measured precision", Reading("unknown", 50).ReadingPrecisionText == "精度未知");
            foreach (int boundary in new[] { 0, 100 })
                check("estimated boundary " + boundary, new DeviceBatteryInfo("edge", "edge", "", "", boundary, true, false, estimate.LastSeen, BatteryReadingSource.VoltageEstimate).LevelText == "约 " + boundary + "%");
            check("missing level never displays approximate number", new DeviceBatteryInfo("empty", "empty", "", "", null, true, false, estimate.LastSeen, BatteryReadingSource.VoltageEstimate).LevelText == "--");
            var estimateSnapshot = new BatterySnapshot(new[] { estimate }, DateTime.Now, null, false);
            check("failure cache preserves estimate source and timestamp", estimateSnapshot.WithFailure("test").Devices[0].ReadingSource == BatteryReadingSource.VoltageEstimate && estimateSnapshot.WithFailure("test").Devices[0].LastSeen == estimate.LastSeen);
            check("tray preserves estimate qualifier", TrayTextFormatter.BuildFull(estimateSnapshot, false).Contains("约 50%"));
            using (var provider = new HidBatteryProvider())
            {
                provider.ApplyCache(new[] { estimate });
                var cached = provider.ApplyCache(new DeviceBatteryInfo[0])[0];
                check("HID cache preserves estimate qualifier", cached.LevelText == "约 50%" && cached.IsConnectionUnknown);
                var direct = HidBatteryProvider.MakeDevice(info, "direct", 50, "mouse");
                provider.ApplyCache(new[] { direct });
                check("direct HID recovery uses current source", provider.ApplyCache(new[] { direct }).Single(d => d.Id == direct.Id).ReadingSource == BatteryReadingSource.DeviceReported);
            }
            using (var provider = new BluetoothBatteryProvider())
            {
                provider.ResolveReading("source", "BLE", "蓝牙 LE", "", 50, true, estimate.LastSeen, BatteryReadingSource.BluetoothGatt);
                check("Bluetooth fallback preserves original source", provider.ResolveReading("source", "BLE", "蓝牙 LE", "", null, true, DateTime.Now).ReadingSource == BatteryReadingSource.BluetoothGatt);
                check("Windows fallback replaces source even at same percentage", provider.ResolveReading("source", "BLE", "蓝牙 LE", "", 50, true, DateTime.Now).ReadingSource == BatteryReadingSource.WindowsProperty);
            }
            int parsedLevel; BatteryReadingSource parsedSource;
            check("Logitech register percent provenance", HidBatteryProvider.LogitechChannel.TryParseRegister(new byte[] { 0x10, 1, 0x81, 0x0D, 50 }, 1, 0x0D, out parsedLevel, out parsedSource) && parsedLevel == 50 && parsedSource == BatteryReadingSource.DeviceReported);
            check("Logitech register coarse provenance", HidBatteryProvider.LogitechChannel.TryParseRegister(new byte[] { 0x10, 1, 0x81, 0x07, 5 }, 1, 0x07, out parsedLevel, out parsedSource) && parsedLevel == 50 && parsedSource == BatteryReadingSource.CoarseLevel);
            check("invalid register does not retain previous provenance", !HidBatteryProvider.LogitechChannel.TryParseRegister(new byte[] { 0x10, 1, 0x81, 0x07, 9 }, 1, 0x07, out parsedLevel, out parsedSource) && parsedSource == BatteryReadingSource.Unknown);
            var canceled = new TaskCompletionSource<IReadOnlyList<DeviceBatteryInfo>>();
            canceled.SetCanceled();
            var freshTask = Task.FromResult<IReadOnlyList<DeviceBatteryInfo>>(new[] { Reading("fresh", 10) });
            var oldReadings = new[] { Reading("old", 60) };
            var emptyReadings = new DeviceBatteryInfo[0];
            var partial = BatteryMonitorService.CollectResultsAsync(canceled.Task, freshTask,
                oldReadings, emptyReadings, CancellationToken.None).GetAwaiter().GetResult();
            check("timeout preserves successful HID result", partial.Devices.Any(d => d.Id == "fresh" && !d.IsStale && d.Level == 10));
            check("timeout preserves cache timestamp", partial.Devices.Single(d => d.Id == "old").IsStale && partial.Devices.Single(d => d.Id == "old").LastSeen == oldReadings[0].LastSeen);
            check("partial timeout is visible", partial.Error.Contains("蓝牙") && partial.Error.Contains("超时"));
            var reverse = BatteryMonitorService.CollectResultsAsync(freshTask, canceled.Task,
                emptyReadings, oldReadings, CancellationToken.None).GetAwaiter().GetResult();
            check("timeout preserves successful Bluetooth result", reverse.Devices.Any(d => d.Id == "fresh" && !d.IsStale));
            var allTimeout = BatteryMonitorService.CollectResultsAsync(canceled.Task, canceled.Task,
                oldReadings, emptyReadings, CancellationToken.None).GetAwaiter().GetResult();
            check("all timeout preserves only stale data", allTimeout.Devices.Count == 1 && allTimeout.Devices.All(d => d.IsStale) && allTimeout.Error.Contains("2.4G"));
            var recovered = BatteryMonitorService.CollectResultsAsync(freshTask,
                Task.FromResult<IReadOnlyList<DeviceBatteryInfo>>(emptyReadings), oldReadings, oldReadings,
                CancellationToken.None).GetAwaiter().GetResult();
            check("successful scan clears error and obsolete cache", recovered.Error == null && recovered.Devices.Count == 1 && !recovered.Devices[0].IsStale);
            bool shutdownCanceled = false;
            try { BatteryMonitorService.CollectResultsAsync(freshTask, canceled.Task, oldReadings,
                emptyReadings, new CancellationToken(true)).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { shutdownCanceled = true; }
            check("shutdown still cancels publication", shutdownCanceled);
            var failed = new TaskCompletionSource<IReadOnlyList<DeviceBatteryInfo>>();
            failed.SetException(new InvalidOperationException("test failure"));
            var failure = BatteryMonitorService.CollectResultsAsync(failed.Task, freshTask,
                oldReadings, emptyReadings, CancellationToken.None).GetAwaiter().GetResult();
            check("channel exception preserves healthy results", failure.Devices.Count == 2 && failure.Error.Contains("test failure"));
            int partialAlerts = 0;
            var partialNotifier = new NotificationService(null, new AppSettings { LowBatteryThreshold = 20, NotificationsEnabled = true, NotificationSound = false });
            partialNotifier.ToastPresenter = (devices, threshold) => partialAlerts++;
            partialNotifier.Process(partial);
            partialNotifier.Process(allTimeout);
            check("partial result alerts only fresh low batteries", partialAlerts == 1);

            using (var bluetooth = new BluetoothBatteryProvider())
            {
                DateTime originalTime = new DateTime(2026, 1, 1);
                DateTime nextTime = originalTime.AddMinutes(1);
                var original = bluetooth.ResolveReading("bt:one", "耳机", "经典蓝牙", "", 60, true, originalTime);
                var cachedBluetooth = bluetooth.ResolveReading("BT:ONE", "耳机", "经典蓝牙", "", null, true, nextTime);
                check("Bluetooth missing property preserves cached battery", cachedBluetooth.Level == 60 && cachedBluetooth.IsStale);
                check("Bluetooth fallback preserves original timestamp", cachedBluetooth.LastSeen == originalTime);
                check("Bluetooth cached connection is explicitly uncertain", cachedBluetooth.StateText.Contains("缓存"));
                var offlineBluetooth = bluetooth.ResolveReading("bt:one", "耳机", "经典蓝牙", "", 60, false, nextTime);
                check("Bluetooth disconnected property does not refresh timestamp", !offlineBluetooth.IsConnected && offlineBluetooth.IsStale && offlineBluetooth.LastSeen == originalTime);
                var restoredBluetooth = bluetooth.ResolveReading("bt:one", "耳机", "经典蓝牙", "", 59, true, nextTime);
                check("Bluetooth recovery replaces cached value and time", restoredBluetooth.Level == 59 && !restoredBluetooth.IsStale && restoredBluetooth.LastSeen == nextTime);
                check("Bluetooth unknown missing battery is not invented", bluetooth.ResolveReading("bt:unknown", "unknown", "蓝牙 LE", "", null, true, nextTime) == null);
                bluetooth.ResolveReading("bt:two", "鼠标", "蓝牙 LE", "", 0, true, originalTime);
                var secondBluetooth = bluetooth.ResolveReading("bt:two", "鼠标", "蓝牙 LE", "", null, true, nextTime);
                check("Bluetooth cache isolates devices and preserves zero", secondBluetooth.Level == 0 && secondBluetooth.LastSeen == originalTime);
                bluetooth.Dispose();
                check("Bluetooth dispose clears cache", bluetooth.ResolveReading("bt:one", "耳机", "经典蓝牙", "", null, true, nextTime) == null);
            }

            var xinmeng = new HidDeviceInfo { VendorId = 0x3151, ProductId = 0x5045,
                UsagePage = 0xFFFF, Usage = 2, FeatureReportLength = 65, OutputReportLength = 0 };
            check("XINMENG feature-only interface is discoverable", HidBatteryProvider.IsCandidate(xinmeng));
            xinmeng.Usage = 1;
            check("XINMENG input interface excluded", !HidBatteryProvider.IsCandidate(xinmeng));
            xinmeng.Usage = 2; xinmeng.ProductId = 0xFFFF;
            check("unverified feature receiver excluded", !HidBatteryProvider.IsCandidate(xinmeng));
            var status = new byte[65]; status[3] = 98; status[4] = 1; status[6] = 1; status[7] = 2; status[10] = 1;
            int xinmengLevel;
            check("XINMENG captured online response is 98 percent", HidBatteryProvider.TryParseXinmengBattery(status, out xinmengLevel) && xinmengLevel == 98);
            status[3] = 0;
            check("XINMENG zero battery accepted", HidBatteryProvider.TryParseXinmengBattery(status, out xinmengLevel) && xinmengLevel == 0);
            status[3] = 100; status[7] = 3;
            check("XINMENG combined receiver and full battery accepted", HidBatteryProvider.TryParseXinmengBattery(status, out xinmengLevel) && xinmengLevel == 100);
            status[3] = 101;
            check("XINMENG invalid percentage rejected", !HidBatteryProvider.TryParseXinmengBattery(status, out xinmengLevel));
            status[3] = 98; status[5] = 1;
            check("XINMENG sleeping mouse not reported connected", !HidBatteryProvider.TryParseXinmengBattery(status, out xinmengLevel));
            status[5] = 0; status[9] = 1;
            check("XINMENG boot mode rejected", !HidBatteryProvider.TryParseXinmengBattery(status, out xinmengLevel));
            status[9] = 0; status[7] = 1;
            check("XINMENG keyboard-only response rejected", !HidBatteryProvider.TryParseXinmengBattery(status, out xinmengLevel));
            status[7] = 2; status[0] = 1;
            check("XINMENG unexpected report ID rejected", !HidBatteryProvider.TryParseXinmengBattery(status, out xinmengLevel));
            check("XINMENG empty and short responses rejected", !HidBatteryProvider.TryParseXinmengBattery(null, out xinmengLevel) && !HidBatteryProvider.TryParseXinmengBattery(new byte[9], out xinmengLevel) && !HidBatteryProvider.TryParseXinmengBattery(new byte[65], out xinmengLevel));

            var mouse = new DeviceBatteryInfo("mouse", "PRO X2 SUPERSTRIKE", "2.4G", "", 70, true, false, DateTime.Now);
            string tooltip = TrayTextFormatter.Build(new BatterySnapshot(new[] { mouse }, DateTime.Now, null, false), true);
            check("tray tooltip exact requested format", tooltip == "WirelessBatteryMoniter · 1 台\n[2.4G] 鼠标: PRO X2 SUPERSTRIKE 70%电量");
            check("tray tooltip omits minimum", !tooltip.Contains("最低"));
            var many = Enumerable.Range(0, 12).Select(i => Reading("device-" + i, 70)).ToList();
            tooltip = TrayTextFormatter.Build(new BatterySnapshot(many, DateTime.Now, null, false), true);
            check("tray tooltip native length bound", tooltip.Length <= 127);
            check("tray tooltip overflow is explicit", tooltip.Contains("点击查看") && tooltip.Contains("70%电量"));
            tooltip = TrayTextFormatter.Build(new BatterySnapshot(new[] { mouse, Reading("offline", 30, false, true) }, DateTime.Now, null, false), false);
            check("tray tooltip respects disconnected preference", !tooltip.Contains("未连接") && tooltip.Split('\n').Length == 2);
            check("tray device type unknown is not guessed", TrayTextFormatter.DeviceType("Logitech mystery") == "设备");
            var fourNames = new[] { "VXE NordicMouse 1K Dongle", "PRO X 2", "PRO X2 SUPERSTRIKE", "XINMENG 2.4G 鼠标" };
            var four = fourNames.Select((name, i) => new DeviceBatteryInfo("tip-" + i, name, "2.4G", "", 70, true, false, DateTime.Now)).ToList();
            string fullTip = TrayTextFormatter.BuildFull(new BatterySnapshot(four, DateTime.Now, null, false), true);
            check("full tray text preserves all four complete names", fourNames.All(name => fullTip.Contains("鼠标: " + name + " 70%电量")) && fullTip.Split('\n').Length == 5);
            check("full tray text exceeds shell limit without ellipsis", fullTip.Length > 127 && !fullTip.Contains("…"));
            check("explicit headset beats ambiguous PRO X name", TrayTextFormatter.DeviceType("PRO X 2 headset") == "耳机");
            check("full tray text respects disconnected preference", !TrayTextFormatter.BuildFull(new BatterySnapshot(new[] { mouse, Reading("offline", 30, false, true) }, DateTime.Now, null, false), false).Contains("offline"));
            var first = new HidDeviceInfo { Path = @"\\?\hid#vid_046d&pid_c54d&mi_02&col01#7&aaaa&0&0000#{guid}" };
            var secondCollection = new HidDeviceInfo { Path = @"\\?\hid#vid_046d&pid_c54d&mi_02&col02#7&aaaa&0&0001#{guid}" };
            var otherReceiver = new HidDeviceInfo { Path = @"\\?\hid#vid_046d&pid_c54d&mi_02&col01#7&bbbb&0&0000#{guid}" };
            var otherVendor = new HidDeviceInfo { Path = @"\\?\hid#vid_373b&pid_1220#7&cccc&0&0000#{guid}" };
            check("HID collections share receiver key", first.PhysicalKey == secondCollection.PhysicalKey);
            check("HID identical models retain receiver identity", first.PhysicalKey != otherReceiver.PhysicalKey);
            check("HID vendors remain separate", first.PhysicalKey != otherVendor.PhysicalKey);
            check("HID null path is safe", new HidDeviceInfo().PhysicalKey == "");

            using (var hid = new HidBatteryProvider())
            {
                var compx = HidBatteryProvider.MakeDevice(otherVendor, "VXE NordicMouse 1K Dongle", 15, "compx");
                var narrow = HidBatteryProvider.MakeDevice(otherVendor, compx.Name, 14, "atk17");
                var wide = HidBatteryProvider.MakeDevice(otherVendor, compx.Name, 13, "atk64");
                check("alternate mouse protocols share identity", compx.Id == narrow.Id && narrow.Id == wide.Id);
                hid.ApplyCache(new[] { compx });
                hid.ApplyCache(new DeviceBatteryInfo[0]);
                var wake = hid.ApplyCache(new[] { narrow });
                check("protocol switch during debounce replaces cached row", wake.Count == 1 && wake[0].Level == 14 && !wake[0].IsStale);
                for (int miss = 0; miss < 10; miss++) hid.ApplyCache(new DeviceBatteryInfo[0]);
                wake = hid.ApplyCache(new[] { wide });
                check("protocol switch after sleep removes disconnected row", wake.Count == 1 && wake[0].IsConnected && !wake[0].IsStale && wake[0].Level == 13);
                wake = hid.ApplyCache(new[] { compx });
                check("reverse protocol switch retains one row", wake.Count == 1 && wake[0].Level == 15);
                var identicalReceiver = new HidDeviceInfo { Path = otherVendor.Path.Replace("cccc", "dddd") };
                var otherMouse = HidBatteryProvider.MakeDevice(identicalReceiver, compx.Name, 80, "compx");
                wake = hid.ApplyCache(new[] { narrow, otherMouse });
                check("same model on separate receivers is not merged", wake.Count == 2 && narrow.Id != otherMouse.Id);
                check("Logitech paired slots remain separate", HidBatteryProvider.MakeDevice(first, "mouse", 50, "logitech-1").Id != HidBatteryProvider.MakeDevice(first, "mouse", 50, "logitech-2").Id);
            }
            var merged = BatteryMonitorService.MergeDuplicateDevices(new List<DeviceBatteryInfo>
            { Reading("one", 50), Reading("two", 50), Reading("one", 50, false, true) });
            check("same-name devices are preserved", merged.Count == 2);
            check("connected reading beats disconnected duplicate", merged.Single(d => d.Id == "one").IsConnected);
            merged = BatteryMonitorService.MergeDuplicateDevices(new List<DeviceBatteryInfo>
            { Reading("one", 20, true, true), Reading("ONE", 30) });
            check("fresh duplicate replaces stale reading", merged.Count == 1 && merged[0].Level == 30);

            using (var hid = new HidBatteryProvider())
            {
                hid.ApplyCache(new[] { Reading("one", 50), Reading("two", 40) });
                var disconnected = hid.ApplyCache(new[] { Reading("two", 39) });
                var cached = disconnected.Single(d => d.Id == "one");
                check("HID first miss retains pending connection", cached.Level == 50 && cached.IsConnectionUnknown && cached.IsStale);
                check("HID cache preserves reading timestamp", cached.LastSeen == new DateTime(2026, 1, 1));
                var reconnected = hid.ApplyCache(new[] { Reading("one", 49), Reading("two", 38) });
                check("HID reconnect replaces stale state", reconnected.Count == 2 && reconnected.All(d => d.IsConnected && !d.IsStale));
            }

            using (var hid = new HidBatteryProvider())
            {
                int debounceAlerts = 0;
                var debounceNotifier = new NotificationService(null, new AppSettings
                    { LowBatteryThreshold = 20, NotificationsEnabled = true, NotificationSound = false });
                debounceNotifier.ToastPresenter = (devices, threshold) => debounceAlerts++;
                Func<DeviceBatteryInfo[], IReadOnlyList<DeviceBatteryInfo>> scan = readings =>
                {
                    var results = hid.ApplyCache(readings);
                    debounceNotifier.Process(new BatterySnapshot(results, DateTime.Now, null, false));
                    return results;
                };
                scan(new[] { Reading("one", 10), Reading("two", 50) });
                scan(new[] { Reading("two", 49) });
                var secondMiss = scan(new[] { Reading("two", 48) });
                check("HID second miss remains pending", secondMiss.Single(d => d.Id == "one").IsConnectionUnknown && secondMiss.Single(d => d.Id == "one").IsStale);
                scan(new[] { Reading("ONE", 9), Reading("two", 47) });
                check("brief failure recovery does not repeat alert", debounceAlerts == 1);
                var resetMiss = scan(new[] { Reading("two", 46) });
                check("missing reading after recovery is unknown case insensitively", resetMiss.Single(d => d.Id == "ONE").IsConnectionUnknown);
                scan(new[] { Reading("two", 45) });
                var thirdMiss = scan(new[] { Reading("two", 44) });
                var offline = thirdMiss.Single(d => d.Id == "ONE");
                check("third consecutive miss remains unknown", offline.IsConnectionUnknown && offline.IsStale && offline.Level == 9);
                check("debounce preserves last successful timestamp", offline.LastSeen == new DateTime(2026, 1, 1));
                check("miss counts are isolated by device", thirdMiss.Single(d => d.Id == "two").IsConnected && !thirdMiss.Single(d => d.Id == "two").IsStale);
                var stillOffline = scan(new[] { Reading("two", 43) });
                check("continued misses remain unknown", stillOffline.Single(d => d.Id == "ONE").IsConnectionUnknown);
                scan(new[] { Reading("one", 8), Reading("two", 42) });
                check("unknown state recovery does not rearm alert", debounceAlerts == 1);
                for (int miss = 0; miss < 100; miss++) scan(new[] { Reading("two", 42) });
                var unknownResults = scan(new[] { Reading("two", 42) });
                var unknownSnapshot = new BatterySnapshot(unknownResults, DateTime.Now, null, false);
                var unknownDevice = unknownResults.Single(d => d.Id == "one");
                check("long absence stays unknown and preserves last reading", unknownDevice.IsConnectionUnknown && unknownDevice.IsStale && unknownDevice.Level == 8 && unknownDevice.LastSeen == new DateTime(2026, 1, 1));
                check("unknown excluded from connected and disconnected counts", unknownSnapshot.ConnectedCount == 1 && unknownSnapshot.PendingCount == 1 && unknownSnapshot.DisconnectedCount == 0);
                check("unknown cached low battery excluded from current statistics", unknownSnapshot.MinimumLevel == 42 && unknownSnapshot.LowBatteryCount(20) == 0);
                check("unknown stays visible when disconnected devices hidden", TrayTextFormatter.BuildFull(unknownSnapshot, false).Contains("连接状态未知，缓存"));
                check("scan failure preserves unknown state", unknownSnapshot.WithFailure("test").Devices.Single(d => d.Id == "one").IsConnectionUnknown);
                scan(new[] { Reading("one", 8), Reading("two", 42) });
                check("long absence recovery does not repeat alert", debounceAlerts == 1);
                scan(new[] { Reading("one", 8, false, true), Reading("two", 42) });
                var explicitOffline = scan(new[] { Reading("two", 42) });
                check("explicit disconnection survives cache fallback", explicitOffline.Single(d => d.Id == "one").IsDisconnected);
                scan(new[] { Reading("one", 8), Reading("two", 42) });
                check("explicit disconnect recovery rearms alert", debounceAlerts == 2);
            }

            var settings = new AppSettings { LowBatteryThreshold = 20, NotificationsEnabled = true, NotificationSound = false };
            var notifier = new NotificationService(null, settings);
            int alerts = 0;
            notifier.ToastPresenter = (devices, threshold) => alerts++;
            Action<int, bool, bool> feed = (level, connected, stale) =>
                notifier.Process(new BatterySnapshot(new[] { Reading("one", level, connected, stale) }, DateTime.Now, null, false));
            feed(20, true, false);
            feed(20, true, false);
            check("low battery alerts once", alerts == 1);
            feed(21, true, false);
            feed(20, true, false);
            feed(25, true, false);
            feed(20, true, false);
            check("threshold hysteresis survives 20-21-20 and 20-25-20", alerts == 1);
            feed(26, true, false);
            feed(20, true, false);
            check("recovery above threshold plus five rearms", alerts == 2);
            feed(20, false, true);
            feed(20, true, false);
            check("disconnect and reconnect rearms", alerts == 3);
            notifier.Reset();
            feed(10, true, true);
            check("stale readings do not trigger alerts", alerts == 3);
            feed(10, true, false);
            check("fresh reading after stale triggers alert", alerts == 4);
            settings.NotificationsEnabled = false;
            feed(10, true, false);
            settings.NotificationsEnabled = true;
            feed(10, true, false);
            check("notification toggle rearms", alerts == 5);
            notifier.Process(new BatterySnapshot(new DeviceBatteryInfo[0], DateTime.Now, null, false));
            feed(10, true, false);
            check("disappeared device rearms", alerts == 6);
        }

        public static int Run()
        {
            AppLog.UseDiagnosticDirectory(Path.Combine(Path.GetTempPath(), "WirelessBatteryMoniter-tests"));
            List<string> lines = new List<string>();
            int failures = 0;

            Action<string, bool> check = (name, ok) =>
            {
                if (ok)
                {
                    lines.Add("PASS  " + name);
                }
                else
                {
                    failures++;
                    lines.Add("FAIL  " + name);
                }
            };

            try
            {
                AppSettings settings = new AppSettings
                {
                    PollSeconds = 1,
                    LowBatteryThreshold = 100
                };
                settings.Normalize();
                check("settings clamp poll", settings.PollSeconds == 5);
                check("settings clamp threshold", settings.LowBatteryThreshold == 95);

                check("level normalize low", DeviceBatteryInfo.NormalizeLevel(-1) == null);
                check("level normalize high", DeviceBatteryInfo.NormalizeLevel(101) == null);
                check("level normalize valid", DeviceBatteryInfo.NormalizeLevel(72) == 72);

                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                using (MemoryStream stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, settings);
                    stream.Position = 0;
                    AppSettings roundTrip = (AppSettings)serializer.ReadObject(stream);
                    check("settings json roundtrip", roundTrip != null && roundTrip.PollSeconds == 5);
                }

                string settingsFolder = Path.Combine(Path.GetTempPath(), "WirelessBatteryMoniter-selftest-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(settingsFolder);
                try
                {
                    SettingsService service = new SettingsService(settingsFolder);
                    AppSettings toSave = new AppSettings
                    {
                        LowBatteryThreshold = 35,
                        PollSeconds = 20,
                        NotificationsEnabled = false,
                        StartMinimized = false
                    };
                    string saveError;
                    check("settings save", service.TrySave(toSave, out saveError));
                    AppSettings loaded = service.Load();
                    check("settings load", loaded.LowBatteryThreshold == 35
                        && loaded.PollSeconds == 20
                        && !loaded.NotificationsEnabled
                        && !loaded.StartMinimized);
                }
                finally
                {
                    try { Directory.Delete(settingsFolder, true); }
                    catch { }
                }

                Func<string, AppSettings> parseSettings = json =>
                {
                    using (MemoryStream jsonStream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        DataContractJsonSerializer jsonSerializer = new DataContractJsonSerializer(typeof(AppSettings));
                        return (AppSettings)jsonSerializer.ReadObject(jsonStream);
                    }
                };
                AppSettings sparseSettings = parseSettings("{}");
                check("settings missing members use documented defaults",
                    sparseSettings.LowBatteryThreshold == 20
                    && sparseSettings.PollSeconds == 10
                    && sparseSettings.NotificationsEnabled
                    && sparseSettings.NotificationSound
                    && sparseSettings.ShowDisconnected
                    && sparseSettings.StartMinimized);
                check("existing settings file is not a first run", !sparseSettings.FirstRun);
                AppSettings overriddenSettings = parseSettings("{\"LowBatteryThreshold\":35,\"PollSeconds\":20}");
                check("settings present members override defaults",
                    overriddenSettings.LowBatteryThreshold == 35
                    && overriddenSettings.PollSeconds == 20
                    && overriddenSettings.NotificationsEnabled
                    && !overriddenSettings.FirstRun);

                BatteryMonitorService monitor = new BatteryMonitorService { DemoMode = true };
                BatterySnapshot snapshot = monitor.ScanAsync(CancellationToken.None).GetAwaiter().GetResult();
                monitor.Dispose();
                check("demo device count", snapshot.Devices.Count == 4);
                check("demo connected count", snapshot.ConnectedCount == 3);
                check("demo has 2.4G device", snapshot.Devices.Any(d => d.Kind == "2.4G"));
                check("demo minimum level", snapshot.MinimumLevel == 18);
                check("demo low count", snapshot.LowBatteryCount(20) == 1);
                check("demo sorting lowest first", snapshot.Devices[0].Level == 18);

                check("snapshot low threshold zero", snapshot.LowBatteryCount(10) == 0);
                var pending = new DeviceBatteryInfo("pending", "缓存鼠标", "2.4G", "", 5, true, true, DateTime.Now);
                var fresh = new DeviceBatteryInfo("fresh", "正常鼠标", "2.4G", "", 70, true, false, DateTime.Now);
                var mixed = new BatterySnapshot(new[] { pending, fresh }, DateTime.Now, null, false);
                check("cached device excluded from confirmed count", mixed.ConnectedCount == 1 && mixed.PendingCount == 1);
                check("cached low reading excluded from current statistics", mixed.MinimumLevel == 70 && mixed.LowBatteryCount(20) == 0 && mixed.KnownBatteryCount == 1);
                check("tray includes pending and preserved reading", TrayTextFormatter.BuildFull(mixed, false).Contains("1 台待确认") && TrayTextFormatter.BuildFull(mixed, false).Contains("5%电量（待确认，缓存）"));
                var onlyCached = new BatterySnapshot(new[] { pending }, DateTime.Now, null, false);
                check("all cached has no current battery", onlyCached.ConnectedCount == 0 && onlyCached.PendingCount == 1 && !onlyCached.MinimumLevel.HasValue);
                check("cached tooltip fits native buffer", TrayTextFormatter.Build(onlyCached, false).Length <= TrayTextFormatter.MaximumLength);
                var failed = mixed.WithFailure("测试刷新异常");
                check("unexpected failure retains readings and timestamps", failed.Devices.Count == 2 && failed.Devices.All(d => d.IsStale) && failed.Devices.Single(d => d.Id == fresh.Id).LastSeen == fresh.LastSeen && failed.Devices.Single(d => d.Id == fresh.Id).Level == 70);
                check("unexpected failure invalidates current statistics", failed.ConnectedCount == 0 && failed.PendingCount == 2 && !failed.MinimumLevel.HasValue);
                check("unexpected failure visible in tray", TrayTextFormatter.BuildFull(failed, true).Contains("读取异常") && failed.Error == "测试刷新异常");
                check("first scan failure remains visible", TrayTextFormatter.BuildFull(BatterySnapshot.Empty(false).WithFailure("首次失败"), false).Contains("读取异常"));
                using (Bitmap normal = TrayIconRenderer.CreateBitmap(1, 30, 20))
                using (Bitmap low = TrayIconRenderer.CreateBitmap(1, 30, 40))
                using (Bitmap restored = TrayIconRenderer.CreateBitmap(1, 30, 20))
                    check("threshold changes recolor same reading", normal.GetPixel(5,17) != low.GetPixel(5,17) && normal.GetPixel(5,17) == restored.GetPixel(5,17));



                using (Bitmap bitmap = TrayIconRenderer.CreateBitmap(3, 18, 20))
                {
                    check("tray bitmap size", bitmap.Width == 32 && bitmap.Height == 32);
                    check("tray bitmap has pixels", bitmap.GetPixel(5, 17).A > 0);
                }

                using (Icon icon = TrayIconRenderer.Create(3, 18, 20))
                {
                    check("tray icon created", icon != null && icon.Width > 0);
                }

                check("theme battery color", Theme.BatteryColor(5, 20) == Theme.Red);
                check("log directory", !string.IsNullOrEmpty(AppLog.DirectoryPath));
                RunRegressionChecks(check);

            }
            catch (Exception ex)
            {
                failures++;
                lines.Add("FAIL  exception: " + ex);
            }

            lines.Add(string.Empty);
            lines.Add(failures == 0 ? "RESULT: ALL TESTS PASSED" : "RESULT: " + failures + " FAILURE(S)");

            string path = Path.Combine(Path.GetTempPath(), "WirelessBatteryMoniter-selftest.txt");
            try
            {
                File.WriteAllLines(path, lines, Encoding.UTF8);
            }
            catch
            {
            }

            return failures == 0 ? 0 : 1;
        }
    }
}


