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
        };

        public static void ValidateForApply(MigrationPlan plan)
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
            }
        }
    }
}
