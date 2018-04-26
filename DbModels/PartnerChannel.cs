namespace FortressCodesApi.DbModels
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    [Table("PartnerChannel")]
    public partial class PartnerChannel
    {
        [Key]
        [Column(Order = 0)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int partnerchannelid { get; set; }

        [Key]
        [Column(Order = 1)]
        public string partnerid { get; set; }

        [Required]
        [StringLength(50)]
        public string partnerchannelname { get; set; }

        public int? channelId { get; set; }

        public virtual AspNetUser AspNetUser { get; set; }

        public virtual Channel Channel { get; set; }
    }
}
