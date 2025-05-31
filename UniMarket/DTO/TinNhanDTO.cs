namespace UniMarket.DTO
{
    public class TinNhanDTO
    {
        public int MaTinNhan { get; set; }
        public string MaCuocTroChuyen { get; set; }
        public string MaNguoiGui { get; set; }
        public string NoiDung { get; set; }
        public DateTime ThoiGianGui { get; set; }
        public bool DaXem { get; set; }
        public DateTime? ThoiGianXem { get; set; }

    }
}
