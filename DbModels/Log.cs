namespace FortressCodesApi.DbModels
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    [Table("Log")]
    public partial class Log
    {
        public int Id { get; set; }

        public DateTime? Date { get; set; }

        [StringLength(50)]
        public string Entity { get; set; }

        public string OldValues { get; set; }

        public string NewValues { get; set; }

        [StringLength(128)]
        public string UserId { get; set; }

        [StringLength(50)]
        public string Type { get; set; }
    }
}
