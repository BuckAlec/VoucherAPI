namespace FortressCodesApi.DbModels
{
    using System;
    using System.Data.Entity;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Linq;

    public partial class FortressCodesContext : DbContext
    {
        public FortressCodesContext()
            : base("name=FortressCodesContext")
        {
        }

        public virtual DbSet<AspNetRole> AspNetRoles { get; set; }
        public virtual DbSet<AspNetUserClaim> AspNetUserClaims { get; set; }
        public virtual DbSet<AspNetUserLogin> AspNetUserLogins { get; set; }
        public virtual DbSet<AspNetUser> AspNetUsers { get; set; }
        public virtual DbSet<Channel> Channels { get; set; }
        public virtual DbSet<ChannelType> ChannelTypes { get; set; }
        public virtual DbSet<Country> Countries { get; set; }
        public virtual DbSet<Currency> Currencies { get; set; }
        public virtual DbSet<Device> Devices { get; set; }
        public virtual DbSet<DeviceLevel> DeviceLevels { get; set; }
        public virtual DbSet<Family> Families { get; set; }
        public virtual DbSet<Level> Levels { get; set; }
        public virtual DbSet<Log> Logs { get; set; }
        public virtual DbSet<MembershipTier> MembershipTiers { get; set; }
        public virtual DbSet<PartnerChannel> PartnerChannels { get; set; }
        public virtual DbSet<Partner> Partners { get; set; }
        public virtual DbSet<PricingModel> PricingModels { get; set; }
        public virtual DbSet<Status> Status { get; set; }
        public virtual DbSet<Tier> Tiers { get; set; }
        public virtual DbSet<Transaction> Transactions { get; set; }
        public virtual DbSet<TransactionType> TransactionTypes { get; set; }
        public virtual DbSet<UserProfileInfo> UserProfileInfoes { get; set; }
        public virtual DbSet<VoucherMetadata> VoucherMetadatas { get; set; }
        public virtual DbSet<Voucher> Vouchers { get; set; }
        public virtual DbSet<VoucherType> VoucherTypes { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AspNetRole>()
                .HasMany(e => e.AspNetUsers)
                .WithMany(e => e.AspNetRoles)
                .Map(m => m.ToTable("AspNetUserRoles").MapLeftKey("RoleId").MapRightKey("UserId"));

            modelBuilder.Entity<AspNetUser>()
                .HasMany(e => e.AspNetUserClaims)
                .WithRequired(e => e.AspNetUser)
                .HasForeignKey(e => e.UserId);

            modelBuilder.Entity<AspNetUser>()
                .HasMany(e => e.AspNetUserLogins)
                .WithRequired(e => e.AspNetUser)
                .HasForeignKey(e => e.UserId);

            modelBuilder.Entity<AspNetUser>()
                .HasMany(e => e.PartnerChannels)
                .WithRequired(e => e.AspNetUser)
                .HasForeignKey(e => e.partnerid)
                .WillCascadeOnDelete(false);

            modelBuilder.Entity<Device>()
                .HasMany(e => e.DeviceLevels)
                .WithRequired(e => e.Device)
                .WillCascadeOnDelete(false);

            modelBuilder.Entity<Level>()
                .HasMany(e => e.DeviceLevels)
                .WithRequired(e => e.Level)
                .WillCascadeOnDelete(false);

            modelBuilder.Entity<Partner>()
                .HasMany(e => e.DeviceLevels)
                .WithOptional(e => e.Partner)
                .HasForeignKey(e => e.PartnerId);

            modelBuilder.Entity<Partner>()
                .HasMany(e => e.PricingModels)
                .WithOptional(e => e.Partner)
                .HasForeignKey(e => e.PartnerId);

            modelBuilder.Entity<Partner>()
                .HasMany(e => e.Tiers)
                .WithOptional(e => e.Partner)
                .HasForeignKey(e => e.PartnerId);

            modelBuilder.Entity<Tier>()
                .HasMany(e => e.PricingModels)
                .WithOptional(e => e.Tier)
                .HasForeignKey(e => e.TeirId);

            modelBuilder.Entity<Tier>()
                .HasMany(e => e.Vouchers)
                .WithOptional(e => e.Tier)
                .HasForeignKey(e => e.membershiptierid);

            modelBuilder.Entity<Transaction>()
                .Property(e => e.ExceptionMessage)
                .IsFixedLength();

            modelBuilder.Entity<UserProfileInfo>()
                .HasMany(e => e.AspNetUsers)
                .WithOptional(e => e.UserProfileInfo)
                .HasForeignKey(e => e.UserProfileInfo_Id)
                .WillCascadeOnDelete();

            modelBuilder.Entity<Voucher>()
                .HasMany(e => e.Transactions)
                .WithOptional(e => e.Voucher)
                .HasForeignKey(e => e.CodeId);
        }
    }
}
