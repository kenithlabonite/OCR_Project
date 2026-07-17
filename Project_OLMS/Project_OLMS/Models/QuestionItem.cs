namespace Project_OLMS.Models
{
    /// <summary>
    /// Represents a single multiple-choice question produced by the AI.
    /// This is a plain data class (not an EF entity); instances are
    /// serialised as JSON and stored in the ExamQuestionnaire.QuestionsJson column.
    /// </summary>
    public class QuestionItem
    {
        /// <summary>Question number, 1–10.</summary>
        public int Number { get; set; }

        /// <summary>The question text.</summary>
        public string QuestionText { get; set; } = string.Empty;

        /// <summary>Choice A.</summary>
        public string OptionA { get; set; } = string.Empty;

        /// <summary>Choice B.</summary>
        public string OptionB { get; set; } = string.Empty;

        /// <summary>Choice C.</summary>
        public string OptionC { get; set; } = string.Empty;

        /// <summary>Choice D.</summary>
        public string OptionD { get; set; } = string.Empty;

        /// <summary>
        /// The letter of the correct choice: "A", "B", "C", or "D".
        /// </summary>
        public string CorrectAnswer { get; set; } = string.Empty;

        /// <summary>
        /// A brief explanation of why the correct answer is right.
        /// </summary>
        public string Explanation { get; set; } = string.Empty;
    }
}
