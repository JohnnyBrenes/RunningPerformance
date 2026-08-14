using Dynastream.Fit;

namespace RunningPerformance.UnitTests;

internal static class SyntheticFitFixture
{
    public static string Create(long serialNumber = 424242)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rp-synthetic-{Guid.NewGuid():N}.fit");
        var start = new Dynastream.Fit.DateTime(
            new System.DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc));
        var end = new Dynastream.Fit.DateTime(start);
        end.Add(10);

        var messages = new List<Mesg>();
        var startEvent = new EventMesg();
        startEvent.SetTimestamp(start);
        startEvent.SetEvent(Event.Timer);
        startEvent.SetEventType(EventType.Start);
        messages.Add(startEvent);

        for (uint index = 0; index <= 10; index++)
        {
            var timestamp = new Dynastream.Fit.DateTime(start);
            timestamp.Add(index);
            var record = new RecordMesg();
            record.SetTimestamp(timestamp);
            record.SetDistance(index * 100f);
            record.SetSpeed(2.75f);
            record.SetHeartRate((byte)(140 + index));
            record.SetCadence(85);
            record.SetPower((ushort)(210 + index));
            record.SetAltitude(2240f + index);
            record.SetPositionLat((int)(232000000 + index * 100));
            record.SetPositionLong((int)(-1184000000 + index * 100));
            messages.Add(record);
        }

        var stopEvent = new EventMesg();
        stopEvent.SetTimestamp(end);
        stopEvent.SetEvent(Event.Timer);
        stopEvent.SetEventType(EventType.StopAll);
        messages.Add(stopEvent);

        var lap = new LapMesg();
        lap.SetMessageIndex(0);
        lap.SetTimestamp(end);
        lap.SetStartTime(start);
        lap.SetTotalElapsedTime(10);
        lap.SetTotalTimerTime(10);
        lap.SetTotalDistance(1000);
        lap.SetAvgHeartRate(145);
        lap.SetMaxHeartRate(150);
        messages.Add(lap);

        var session = new SessionMesg();
        session.SetMessageIndex(0);
        session.SetTimestamp(end);
        session.SetStartTime(start);
        session.SetTotalElapsedTime(10);
        session.SetTotalTimerTime(10);
        session.SetTotalDistance(1000);
        session.SetEnhancedAvgSpeed(2.75f);
        session.SetSport(Sport.Running);
        session.SetSubSport(SubSport.Generic);
        session.SetFirstLapIndex(0);
        session.SetNumLaps(1);
        session.SetAvgHeartRate(145);
        session.SetMaxHeartRate(150);
        session.SetAvgCadence(85);
        session.SetAvgPower(215);
        session.SetTotalAscent(12);
        session.SetTotalCalories(50);
        messages.Add(session);

        var activity = new ActivityMesg();
        activity.SetTimestamp(end);
        activity.SetNumSessions(1);
        activity.SetLocalTimestamp(start.GetTimeStamp() - 6 * 60 * 60);
        activity.SetTotalTimerTime(10);
        messages.Add(activity);

        var fileId = new FileIdMesg();
        fileId.SetType(Dynastream.Fit.File.Activity);
        fileId.SetManufacturer(Manufacturer.Development);
        fileId.SetProduct(1);
        fileId.SetSerialNumber(checked((uint)serialNumber));
        fileId.SetTimeCreated(start);

        var device = new DeviceInfoMesg();
        device.SetDeviceIndex(DeviceIndex.Creator);
        device.SetManufacturer(Manufacturer.Development);
        device.SetProduct(1);
        device.SetProductName("Synthetic fixture");
        device.SetSerialNumber(checked((uint)serialNumber));
        device.SetSoftwareVersion(1f);
        device.SetTimestamp(start);

        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        var encoder = new Encode(ProtocolVersion.V20);
        encoder.Open(output);
        encoder.Write(fileId);
        encoder.Write(device);
        foreach (var message in messages)
        {
            encoder.Write(message);
        }
        encoder.Close();
        return path;
    }
}
