using LineMessage.Model;
using Microsoft.EntityFrameworkCore;

namespace LineMessage.Data;

public class LineBotDbContext : DbContext
{
    public DbSet<GroupChat> GroupChats { get; set; }
    public DbSet<UserChat> UserChats { get; set; }
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite("Data Source=LineBot.db");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GroupChat>()
            .HasKey(g => g.GroupId);
        modelBuilder.Entity<UserChat>()
            .HasKey(g => g.UserId);
    }
}