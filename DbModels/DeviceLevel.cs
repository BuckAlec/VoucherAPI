namespace FortressCodesApi.DbModels
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    [Table("DeviceLevel")]
    public partial class DeviceLevel
    {
        public int Id { get; set; }

        public int DeviceId { get; set; }

        public int LevelId { get; set; }

        public int? PartnerId { get; set; }

        public virtual Device Device { get; set; }

        public virtual Partner Partner { get; set; }

        public virtual Level Level { get; set; }
    }
}
