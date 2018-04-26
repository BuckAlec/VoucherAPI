namespace FortressCodesApi.DbModels
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("PricingModel")]
    public partial class PricingModel
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public PricingModel()
        {
            VoucherMetadatas = new HashSet<VoucherMetadata>();
        }

        public int Id { get; set; }

        public int? TeirId { get; set; }

        public int? LevelId { get; set; }

        public decimal? AnnualPrice { get; set; }

        public decimal? MonthlyPrice { get; set; }

        public decimal? DailyPrice { get; set; }

        public decimal? ExcessPrice { get; set; }

        public decimal? ReplacementPrice { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public bool? Active { get; set; }

        public int? CountryID { get; set; }

        public int? PartnerId { get; set; }

        public int? FamilyId { get; set; }

        public virtual Country Country { get; set; }

        public virtual Family Family { get; set; }

        public virtual Level Level { get; set; }

        public virtual Partner Partner { get; set; }

        public virtual Tier Tier { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<VoucherMetadata> VoucherMetadatas { get; set; }
    }
}
