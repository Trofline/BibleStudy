using BibleStudy.Core.Storage;

namespace BibleStudy.Tests;

public class ImageTests
{
    [Fact]
    public void A_picture_is_kept_once_and_comes_back_and_a_merge_brings_it_along()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bs_img_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var home = new VaultStore(Path.Combine(dir, "home.db"), "home");
        var guestPath = Path.Combine(dir, "guest.db");
        var guest = new VaultStore(guestPath, "guest");
        byte[] picture = [137, 80, 78, 71, 1, 2, 3, 4, 5];

        var id = guest.AddImage(picture, "image/png");
        Assert.Equal(id, guest.AddImage(picture, "image/png"));   // the same picture: the same id, stored once
        var back = guest.GetImage(id);
        Assert.Equal("image/png", back!.Value.Mime);
        Assert.Equal(picture, back.Value.Data);
        Assert.Null(home.GetImage(id));

        home.MergeFrom(guestPath, "Gast");
        Assert.Equal(picture, home.GetImage(id)!.Value.Data);
    }
}
