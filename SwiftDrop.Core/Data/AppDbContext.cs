using Microsoft.EntityFrameworkCore;
using SwiftDrop.Core.Models;

namespace SwiftDrop.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<FileTransfer> FileTransfers => Set<FileTransfer>();
}