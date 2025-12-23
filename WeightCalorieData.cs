namespace WeightCalorieMAUI
{
    /// <summary>
    /// Represents a weight and calorie record.
    /// </summary>
    public class WeightCalorieData
    {
        /// <summary>
        /// Gets or sets the date of the record.
        /// </summary>
        public required string Date { get; set; }

        /// <summary>
        /// Gets or sets the weight value.
        /// </summary>
        public required string Weight { get; set; }

        /// <summary>
        /// Gets or sets the calorie intake value.
        /// </summary>
        public required string Calorie { get; set; }
    }
}