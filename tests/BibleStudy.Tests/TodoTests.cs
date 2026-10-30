using BibleStudy.Core.Models;
using BibleStudy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace BibleStudy.Tests;

public class TodoTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bs_" + Guid.NewGuid().ToString("N"));
    readonly VaultStore _v;

    public TodoTests()
    {
        Directory.CreateDirectory(_dir);
        _v = new VaultStore(Path.Combine(_dir, "v.db"), "T");
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Tasks_KeepTheirOrder_AndCanBeMoved()
    {
        _v.AddTodo("A", null); _v.AddTodo("B", null); _v.AddTodo("C", null);
        Assert.Equal(["A", "B", "C"], _v.GetTodos().Select(t => t.Text));
        var c = _v.GetTodos()[2].Id;
        _v.MoveTodo(c, -1);
        Assert.Equal(["A", "C", "B"], _v.GetTodos().Select(t => t.Text));
        _v.MoveTodo(c, -1); _v.MoveTodo(c, -1);                    // already first: stays
        Assert.Equal(["C", "A", "B"], _v.GetTodos().Select(t => t.Text));
    }

    [Fact]
    public void SubTasks_BelongToTheirTask_TickWithIt_AndGoWithIt()
    {
        _v.AddTodo("Römer 8", new VerseRef("ROM", 8, 28));
        var parent = _v.GetTodos().Single().Id;
        _v.AddTodo("Vers 1 lesen", null, parent); _v.AddTodo("Vers 28 lesen", null, parent);
        Assert.All(_v.GetTodos().Skip(1), t => Assert.Equal(parent, t.ParentId));

        _v.SetTodoDone(parent, true);                              // ticking the task ticks its sub-tasks
        Assert.All(_v.GetTodos(), t => Assert.True(t.Done));
        _v.SetTodoDone(parent, false);                             // taking the tick away leaves them
        Assert.True(_v.GetTodos().Single(t => t.Id == parent).Done == false);
        Assert.All(_v.GetTodos().Where(t => t.ParentId == parent), t => Assert.True(t.Done));

        _v.DeleteTodo(parent);
        Assert.Empty(_v.GetTodos());
    }

    [Fact]
    public void DueDate_TextAndVerse_CanBeChanged_AndDoneThingsCanBeCleared()
    {
        _v.AddTodo("Predigt", null, null, new DateOnly(2026, 11, 7));
        _v.AddTodo("Fertig", null);
        var t = _v.GetTodos();
        Assert.Equal(new DateOnly(2026, 11, 7), t[0].Due);
        _v.SetTodoDue(t[0].Id, null); _v.SetTodoText(t[0].Id, "  Predigt vorbereiten ");
        _v.SetTodoRef(t[0].Id, new VerseRef("JHN", 3, 16));
        var changed = _v.GetTodos()[0];
        Assert.Equal(("Predigt vorbereiten", null, new VerseRef("JHN", 3, 16)), (changed.Text, changed.Due, changed.Ref));

        _v.SetTodoDone(t[1].Id, true);
        _v.ClearDoneTodos();
        Assert.Equal(["Predigt vorbereiten"], _v.GetTodos().Select(x => x.Text));
    }

    [Fact]
    public void OldVaults_AreMigrated()
    {
        var path = Path.Combine(_dir, "old.db");
        using (var con = new SqliteConnection($"Data Source={path}"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "CREATE TABLE todos(id INTEGER PRIMARY KEY, text TEXT, done INT, book TEXT, chapter INT, verse INT, created TEXT); INSERT INTO todos(text,done) VALUES('alt',0),('auch alt',1)";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        var old = new VaultStore(path, "Alt");
        Assert.Equal(["alt", "auch alt"], old.GetTodos().Select(t => t.Text));
        old.AddTodo("neu", null);
        Assert.Equal("neu", old.GetTodos()[^1].Text);              // lands behind the old ones
    }
}
