using System.Xml.Linq;
using BibleStudy.Core;
using Xunit;

namespace BibleStudy.Tests;

public class SyncFoldersTests
{
    [Fact]
    public void FoldersAreReadFromTheSyncthingConfiguration()
    {
        var config = XDocument.Parse("""
            <configuration version="37">
              <folder id="abcd-1234" label="Dokumente" path="C:\Sync\Dokumente" type="sendreceive"/>
              <folder id="efgh-5678" label="" path="/home/x/Musik/" type="sendonly"/>
              <folder id="nopath" label="Kaputt"/>
              <device id="D1" name="Laptop"/>
            </configuration>
            """);
        var folders = SyncFolders.Parse(config);
        Assert.Equal(2, folders.Count);
        Assert.Equal("Dokumente", folders[0].Label);
        Assert.Equal("Musik", folders[1].Label);   // no label: the folder name
    }

    [Fact]
    public void MissingConfigurationMeansNoFolders() => Assert.Empty(SyncFolders.Find(["/no/such/config.xml"]));
}
