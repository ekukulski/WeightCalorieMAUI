using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;

namespace WeightCalorieMAUI
{
    /// <summary>
    /// Manages weight and calorie calculations.
    /// </summary>
    public class WeightCalorieManager
    {
        /// <summary>
        /// Calculates and updates the average weight loss and average calorie intake.
        /// </summary>
        /// <param name="weights">A list of weight values.</param>
        /// <param name="calories">A list of calorie intake values.</param>
        /// <param name="avgWeightLossLabel">The label to display average weight loss.</param>
        /// <param name="avgCaloriesLabel">The label to display average calories.</param>
        public void CalculateAverages(List<double> weights, List<double> calories, Label avgWeightLossLabel, Label avgCaloriesLabel)
        {
            if (weights.Count > 1)
            {
                double totalWeightLoss = 0;
                for (int i = 1; i < weights.Count; i++)
                {
                    totalWeightLoss += weights[i - 1] - weights[i];
                }
                double avgWeightLoss = totalWeightLoss / (weights.Count - 1);
                avgWeightLossLabel.Text = $" {avgWeightLoss:F2} lbs";
            }
            else
            {
                avgWeightLossLabel.Text = "Average Weight Loss: N/A";
            }

            if (calories.Count > 0)
            {
                double avgCalories = calories.Average();
                avgCaloriesLabel.Text = $" {avgCalories:F2} cal";
            }
            else
            {
                avgCaloriesLabel.Text = "Average Calories: N/A";
            }
        }
    }
}
