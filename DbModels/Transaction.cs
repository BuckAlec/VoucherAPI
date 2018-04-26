namespace FortressCodesApi.DbModels
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    [Table("Transaction")]
    public partial class Transaction
    {
        public int Id { get; set; }

        public int? CodeId { get; set; }

        [StringLength(50)]
        public string RequestCode { get; set; }

        public DateTime? Date { get; set; }

        [StringLength(100)]
        public string UserEmail { get; set; }

        [StringLength(50)]
        public string UserAccountNumber { get; set; }

        [StringLength(50)]
        public string DeviceIMEI { get; set; }

        [StringLength(50)]
        public string DeviceSerialNumber { get; set; }

        [StringLength(50)]
        public string DeviceVendorId { get; set; }

        [StringLength(50)]
        public string DevicePhoneNumber { get; set; }

        [StringLength(50)]
        public string FortressVersion { get; set; }

        [StringLength(50)]
        public string DeviceOS { get; set; }

        public int? TransactionTypeId { get; set; }

        [StringLength(3000)]
        public string ExceptionMessage { get; set; }

        public virtual TransactionType TransactionType { get; set; }

        public virtual Voucher Voucher { get; set; }
    }
}
