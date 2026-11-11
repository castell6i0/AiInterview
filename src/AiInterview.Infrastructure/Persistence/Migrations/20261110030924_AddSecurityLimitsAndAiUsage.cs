using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiInterview.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityLimitsAndAiUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DispatchedAt",
                table: "AiRequestLogs",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeySource",
                table: "AiRequestLogs",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Legacy");

            AddLengthConstraint(migrationBuilder, "TurnScores", "CK_TurnScores_Evidence_Length", "[Evidence] IS NULL OR DATALENGTH([Evidence]) <= 4000");

            AddLengthConstraint(migrationBuilder, "TurnScores", "CK_TurnScores_Feedback_Length", "[Feedback] IS NULL OR DATALENGTH([Feedback]) <= 10000");

            AddLengthConstraint(migrationBuilder, "Turns", "CK_Turns_Answer_Length", "[Answer] IS NULL OR DATALENGTH([Answer]) <= 40000");

            AddLengthConstraint(migrationBuilder, "Turns", "CK_Turns_DraftAnswer_Length", "[DraftAnswer] IS NULL OR DATALENGTH([DraftAnswer]) <= 40000");

            AddLengthConstraint(migrationBuilder, "Turns", "CK_Turns_Question_Length", "[Question] IS NULL OR DATALENGTH([Question]) <= 10000");

            AddLengthConstraint(migrationBuilder, "Turns", "CK_Turns_SpeechEventsJson_Length", "[SpeechEventsJson] IS NULL OR DATALENGTH([SpeechEventsJson]) <= 2000000");

            AddLengthConstraint(migrationBuilder, "TurnEvaluations", "CK_TurnEvaluations_BetterAnswer_Length", "[BetterAnswer] IS NULL OR DATALENGTH([BetterAnswer]) <= 40000");

            AddLengthConstraint(migrationBuilder, "TurnEvaluations", "CK_TurnEvaluations_ChangesExplanation_Length", "[ChangesExplanation] IS NULL OR DATALENGTH([ChangesExplanation]) <= 16000");

            AddLengthConstraint(migrationBuilder, "TurnEvaluations", "CK_TurnEvaluations_CodeReviewJson_Length", "[CodeReviewJson] IS NULL OR DATALENGTH([CodeReviewJson]) <= 500000");

            AddLengthConstraint(migrationBuilder, "TurnEvaluations", "CK_TurnEvaluations_ImprovementsJson_Length", "[ImprovementsJson] IS NULL OR DATALENGTH([ImprovementsJson]) <= 300000");

            AddLengthConstraint(migrationBuilder, "TurnEvaluations", "CK_TurnEvaluations_StrengthsJson_Length", "[StrengthsJson] IS NULL OR DATALENGTH([StrengthsJson]) <= 300000");

            AddLengthConstraint(migrationBuilder, "Sessions", "CK_Sessions_CvSnapshot_Length", "[CvSnapshot] IS NULL OR DATALENGTH([CvSnapshot]) <= 100000");

            AddLengthConstraint(migrationBuilder, "Sessions", "CK_Sessions_JdSnapshot_Length", "[JdSnapshot] IS NULL OR DATALENGTH([JdSnapshot]) <= 60000");

            AddLengthConstraint(migrationBuilder, "Sessions", "CK_Sessions_SkillGapSnapshotJson_Length", "[SkillGapSnapshotJson] IS NULL OR DATALENGTH([SkillGapSnapshotJson]) <= 9000000");

            AddLengthConstraint(migrationBuilder, "Questions", "CK_Questions_Content_Length", "[Content] IS NULL OR DATALENGTH([Content]) <= 8000");

            AddLengthConstraint(migrationBuilder, "LecturerComments", "CK_LecturerComments_Content_Length", "[Content] IS NULL OR DATALENGTH([Content]) <= 6000");

            AddLengthConstraint(migrationBuilder, "JobDescriptions", "CK_JobDescriptions_Content_Length", "[Content] IS NULL OR DATALENGTH([Content]) <= 60000");

            AddLengthConstraint(migrationBuilder, "JobDescriptions", "CK_JobDescriptions_SkillGapJson_Length", "[SkillGapJson] IS NULL OR DATALENGTH([SkillGapJson]) <= 9000000");

            AddLengthConstraint(migrationBuilder, "Evaluations", "CK_Evaluations_Reason_Length", "[Reason] IS NULL OR DATALENGTH([Reason]) <= 16000");

            AddLengthConstraint(migrationBuilder, "Evaluations", "CK_Evaluations_SummaryJson_Length", "[SummaryJson] IS NULL OR DATALENGTH([SummaryJson]) <= 2200000");

            AddLengthConstraint(migrationBuilder, "DeliveryMetrics", "CK_DeliveryMetrics_FillerDetailsJson_Length", "[FillerDetailsJson] IS NULL OR DATALENGTH([FillerDetailsJson]) <= 40000");

            AddLengthConstraint(migrationBuilder, "CvProfiles", "CK_CvProfiles_AnalysisJson_Length", "[AnalysisJson] IS NULL OR DATALENGTH([AnalysisJson]) <= 10000000");

            AddLengthConstraint(migrationBuilder, "CvProfiles", "CK_CvProfiles_ExtractedText_Length", "[ExtractedText] IS NULL OR DATALENGTH([ExtractedText]) <= 100000");

            AddLengthConstraint(migrationBuilder, "Assignments", "CK_Assignments_Instructions_Length", "[Instructions] IS NULL OR DATALENGTH([Instructions]) <= 8000");

            migrationBuilder.CreateIndex(
                name: "IX_AiRequestLogs_UserId_KeySource_CreatedAt",
                table: "AiRequestLogs",
                columns: new[] { "UserId", "KeySource", "CreatedAt" });
        }

        // Reviewed DDL exception: identifiers/predicates are frozen constants above, never request input.
        // Keep old oversized rows unchanged; enforce the bound for all subsequent writes.
        private static void AddLengthConstraint(MigrationBuilder migrationBuilder, string table, string name, string predicate)
        {
            migrationBuilder.Sql($"""
                IF EXISTS (SELECT 1 FROM [{table}] WHERE NOT ({predicate}))
                BEGIN
                    ALTER TABLE [{table}] WITH NOCHECK ADD CONSTRAINT [{name}] CHECK ({predicate});
                    PRINT N'Legacy oversized rows retained; review untrusted length constraints before production.';
                END
                ELSE
                    ALTER TABLE [{table}] WITH CHECK ADD CONSTRAINT [{name}] CHECK ({predicate});
                """);
        }
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TurnScores_Evidence_Length",
                table: "TurnScores");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TurnScores_Feedback_Length",
                table: "TurnScores");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Turns_Answer_Length",
                table: "Turns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Turns_DraftAnswer_Length",
                table: "Turns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Turns_Question_Length",
                table: "Turns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Turns_SpeechEventsJson_Length",
                table: "Turns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TurnEvaluations_BetterAnswer_Length",
                table: "TurnEvaluations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TurnEvaluations_ChangesExplanation_Length",
                table: "TurnEvaluations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TurnEvaluations_CodeReviewJson_Length",
                table: "TurnEvaluations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TurnEvaluations_ImprovementsJson_Length",
                table: "TurnEvaluations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TurnEvaluations_StrengthsJson_Length",
                table: "TurnEvaluations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sessions_CvSnapshot_Length",
                table: "Sessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sessions_JdSnapshot_Length",
                table: "Sessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sessions_SkillGapSnapshotJson_Length",
                table: "Sessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Questions_Content_Length",
                table: "Questions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LecturerComments_Content_Length",
                table: "LecturerComments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobDescriptions_Content_Length",
                table: "JobDescriptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobDescriptions_SkillGapJson_Length",
                table: "JobDescriptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Evaluations_Reason_Length",
                table: "Evaluations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Evaluations_SummaryJson_Length",
                table: "Evaluations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DeliveryMetrics_FillerDetailsJson_Length",
                table: "DeliveryMetrics");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CvProfiles_AnalysisJson_Length",
                table: "CvProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CvProfiles_ExtractedText_Length",
                table: "CvProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Assignments_Instructions_Length",
                table: "Assignments");

            migrationBuilder.DropIndex(
                name: "IX_AiRequestLogs_UserId_KeySource_CreatedAt",
                table: "AiRequestLogs");

            migrationBuilder.DropColumn(
                name: "DispatchedAt",
                table: "AiRequestLogs");

            migrationBuilder.DropColumn(
                name: "KeySource",
                table: "AiRequestLogs");
        }
    }
}
