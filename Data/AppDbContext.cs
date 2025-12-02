
using Microsoft.EntityFrameworkCore;
using Learnit.Server.Models;
using System.Collections.Generic;

namespace Learnit.Server.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions options) : base(options) { }

        public DbSet<User> Users { get; set; }
    }
}
