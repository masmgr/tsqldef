using System;
using System.Collections.Generic;

namespace SqlSchemaDef.Core.Planning
{
    public static class PlanValidator
    {
        private static readonly HashSet<OperationKind> AllowedKinds = new HashSet<OperationKind>
        {
            OperationKind.CreateTable,
            OperationKind.AddColumn,
            OperationKind.AlterColumn,
            OperationKind.RecreateConstraint,
            OperationKind.AddConstraint,
            OperationKind.RecreateIndex,
            OperationKind.CreateIndex,
            OperationKind.AddForeignKey,
            OperationKind.RecreateForeignKey,
            OperationKind.AddDescription,
            OperationKind.UpdateDescription,
            OperationKind.DropDescription,
            OperationKind.DropForeignKey,
            OperationKind.DropIndex,
            OperationKind.DropConstraint,
            OperationKind.DropColumn,
            OperationKind.DropTable,
        };

        public static bool IsAdditiveOperation(OperationKind kind)
        {
            return kind == OperationKind.CreateTable || kind == OperationKind.AddColumn ||
                kind == OperationKind.AddConstraint || kind == OperationKind.CreateIndex ||
                kind == OperationKind.AddForeignKey || kind == OperationKind.AddDescription ||
                kind == OperationKind.UpdateDescription;
        }

        public static void ValidateForApply(MigrationPlan plan)
        {
            ValidateForApply(plan, false);
        }

        public static void ValidateForApply(MigrationPlan plan, bool allowDrop)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));

            for (int i = 0; i < plan.Operations.Count; i++)
            {
                var op = plan.Operations[i];
                if (op == null)
                {
                    continue;
                }

                if (!AllowedKinds.Contains(op.Kind))
                {
                    throw new InvalidOperationException(
                        "Operation " + i + " has unsupported OperationKind " + (int)op.Kind +
                        " (" + op.Kind + "). Only recognized operation kinds are allowed.");
                }

                if (!allowDrop && !IsAdditiveOperation(op.Kind))
                {
                    throw new InvalidOperationException("Operation " + i + " (" + op.Kind + ") requires explicit AllowDrop permission.");
                }
            }
        }
    }
}
