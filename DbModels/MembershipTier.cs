namespace FortressCodesApi.DbModels
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    [Table("MembershipTier")]
    public partial class MembershipTier
    {
        public int id { get; set; }

        public int membershiptierid { get; set; }

        [Required]
        [StringLength(128)]
        public string partnerid { get; set; }

        [Required]
        [StringLength(50)]
        public string membershiptiername { get; set; }
    }
}
