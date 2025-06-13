using System.ComponentModel.DataAnnotations.Schema;
using UniMarket.Models;

public class TinNhanDaXoa
{
    public int Id { get; set; }
    public int TinNhanId { get; set; }
    public string UserId { get; set; }

    [ForeignKey("TinNhanId")]
    public TinNhan TinNhan { get; set; }
}
