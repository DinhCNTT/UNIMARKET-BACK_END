namespace UniMarket.Models
{
    public enum ShareType
    {
        Chat,          // Share qua tin nhắn trong hệ thống
        SocialMedia    // Share ra MXH (FB, Zalo, Messenger, CopyLink...)
    }

    public enum ShareTargetType
    {
        TinDang,   // Share 1 tin đăng
        Video      // Share video (dùng VideoUrl trong TinDang)
    }
}