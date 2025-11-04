using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;

namespace UniMarket.DataAccess // <-- Đảm bảo namespace này khớp với project của bạn
{
    public class DateTimeOffsetConverter : ValueConverter<DateTime, DateTimeOffset>
    {
        public DateTimeOffsetConverter()
            : base(
                csharpValue => new DateTimeOffset(csharpValue, TimeSpan.Zero), // ép UTC
                databaseValue => databaseValue.UtcDateTime // đọc ra cũng UTC
            )
        {
        }
    }
}