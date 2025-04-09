namespace PracticalTools.util;

public class SnowflakeIdGenerator
{
    private const long Epoch = 1609459200000L; // 自定义起始时间戳（2021-01-01 00:00:00 UTC）
    private const int WorkerIdBits = 5;
    private const int DatacenterIdBits = 5;
    private const int SequenceBits = 12;

    private const long MaxWorkerId = -1L ^ (-1L << WorkerIdBits);
    private const long MaxDatacenterId = -1L ^ (-1L << DatacenterIdBits);

    private const int WorkerIdShift = SequenceBits;
    private const int DatacenterIdShift = SequenceBits + WorkerIdBits;
    private const int TimestampLeftShift = SequenceBits + WorkerIdBits + DatacenterIdBits;
    private const long SequenceMask = -1L ^ (-1L << SequenceBits);

    private readonly long _workerId;
    private readonly long _datacenterId;
    private long _sequence = 0L;
    private long _lastTimestamp = -1L;

    public SnowflakeIdGenerator(long workerId, long datacenterId)
    {
        if (workerId > MaxWorkerId || workerId < 0)
        {
            throw new ArgumentException($"Worker ID 不能大于 {MaxWorkerId} 或者小于 0");
        }
        if (datacenterId > MaxDatacenterId || datacenterId < 0)
        {
            throw new ArgumentException($"Datacenter ID 不能大于 {MaxDatacenterId} 或者小于 0");
        }

        _workerId = workerId;
        _datacenterId = datacenterId;
    }

    public long NextId()
    {
        lock (this)
        {
            var timestamp = GetCurrentTimestamp();

            if (timestamp < _lastTimestamp)
            {
                throw new Exception("系统时钟回拨，拒绝生成 ID  " + ( _lastTimestamp - timestamp) + " 毫秒");
            }

            if (timestamp == _lastTimestamp)
            {
                _sequence = (_sequence + 1) & SequenceMask;
                if (_sequence == 0)
                {
                    timestamp = WaitNextMillis(_lastTimestamp);
                }
            }
            else
            {
                _sequence = 0L;
            }

            _lastTimestamp = timestamp;

            return ((timestamp - Epoch) << TimestampLeftShift) |
                   (_datacenterId << DatacenterIdShift) |
                   (_workerId << WorkerIdShift) |
                   _sequence;
        }
    }

    private long GetCurrentTimestamp()
    {
        return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
    }

    private long WaitNextMillis(long lastTimestamp)
    {
        var timestamp = GetCurrentTimestamp();
        while (timestamp <= lastTimestamp)
        {
            timestamp = GetCurrentTimestamp();
        }
        return timestamp;
    }
}