
using Microsoft.EntityFrameworkCore;
using ReactAppTest.Server.Models;
using System.Collections.Generic;

namespace ReactAppTest.Server.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions options) : base(options) { }

        public DbSet<User> Users { get; set; }
    }
}
