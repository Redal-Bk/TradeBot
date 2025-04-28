using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace TradeBot.Entities;

public partial class SlySignalContext : DbContext
{
    public SlySignalContext()
    {
    }

    public SlySignalContext(DbContextOptions<SlySignalContext> options)
        : base(options)
    {
    }

    public virtual DbSet<User> Users { get; set; }
    public virtual DbSet<Tokens> Token { get; set; }
    public virtual DbSet<Coins> Coins { get; set; }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer("server=.;database=SlySignal;TrustServerCertificate=True;Trusted_Connection=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Users__3214EC07FAA9D3C8");

            entity.Property(e => e.FullName).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(false);
            entity.Property(e => e.IsAdmin).HasDefaultValue(false);
            entity.Property(e => e.IsPermium).HasDefaultValue(false);
            entity.Property(e => e.PhoneNumber).HasMaxLength(20);
            entity.Property(e => e.ChatId);
        });

        modelBuilder.Entity<Tokens>(entity => {
            entity.Property(e => e.Token).HasMaxLength(500); 
            
        });
        modelBuilder.Entity<Coins>(entity =>
        {
            //entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(50);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
