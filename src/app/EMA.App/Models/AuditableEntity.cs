namespace EMA.App.Models
{
    public abstract class AuditableEntity
    {
        public int? Sequence { get; set; }
        public DateTime? DateTimeCreated { get; set; }
        public int? CreatedByUserID { get; set; }
        public DateTime? DateTimeUpdated { get; set; }
        public int? UpdatedbyUserID { get; set; }
    }
}