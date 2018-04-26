namespace FortressCodesApi.DbModels
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    public partial class Voucher
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public Voucher()
        {
            Transactions = new HashSet<Transaction>();
            VoucherMetadatas = new HashSet<VoucherMetadata>();
        }

        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string vouchercode { get; set; }

        public bool multiuse { get; set; }

        [Column(TypeName = "date")]
        public DateTime expirydate { get; set; }

        [Required]
        [StringLength(128)]
        public string userid { get; set; }

        public DateTime createddate { get; set; }

        [StringLength(255)]
        public string createduser { get; set; }

        public DateTime? editeddate { get; set; }

        [StringLength(255)]
        public string editeduser { get; set; }

        public bool revoked { get; set; }

        [StringLength(255)]
        public string incentivesource { get; set; }

        public int? incentivemembershiptierid { get; set; }

        public int? incentivemembershiplength { get; set; }

        [StringLength(255)]
        public string deviceid { get; set; }

        [StringLength(500)]
        public string partnernote { get; set; }

        public int batchnumber { get; set; }

        public int? TransactionTypeId { get; set; }

        public int? MaxCount { get; set; }

        public int? membershiplength { get; set; }

        [StringLength(50)]
        public string partnercustomer { get; set; }

        public int? partnerchannelid { get; set; }

        public int? membershiptierid { get; set; }

        public virtual Tier Tier { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<Transaction> Transactions { get; set; }

        public virtual TransactionType TransactionType { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<VoucherMetadata> VoucherMetadatas { get; set; }
    }
}
