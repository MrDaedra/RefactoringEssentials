﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VBasic = Microsoft.CodeAnalysis.VisualBasic;
using VBSyntax = Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace RefactoringEssentials.CSharp.Converter
{
	public partial class VisualBasicConverter
	{
		class MethodBodyVisitor : VBasic.VisualBasicSyntaxVisitor<SyntaxList<StatementSyntax>>
		{
			SemanticModel semanticModel;
			NodesVisitor nodesVisitor;

			public MethodBodyVisitor(SemanticModel semanticModel, NodesVisitor nodesVisitor)
			{
				this.semanticModel = semanticModel;
				this.nodesVisitor = nodesVisitor;
			}

			public override SyntaxList<StatementSyntax> DefaultVisit(SyntaxNode node)
			{
				throw new NotImplementedException(node.GetType() + " not implemented!");
			}

			public override SyntaxList<StatementSyntax> VisitLocalDeclarationStatement(VBSyntax.LocalDeclarationStatementSyntax node)
			{
				var modifiers = ConvertModifiers(node.Modifiers);

				var declarations = new List<LocalDeclarationStatementSyntax>();

				foreach (var declarator in node.Declarators)
					foreach (var decl in SplitVariableDeclarations(declarator, nodesVisitor, semanticModel))
						declarations.Add(SyntaxFactory.LocalDeclarationStatement(modifiers, decl.Value));

				return SyntaxFactory.List<StatementSyntax>(declarations);
			}

			public override SyntaxList<StatementSyntax> VisitExpressionStatement(VBSyntax.ExpressionStatementSyntax node)
			{
				return SingleStatement((ExpressionSyntax)node.Expression.Accept(nodesVisitor));
			}

			public override SyntaxList<StatementSyntax> VisitAssignmentStatement(VBSyntax.AssignmentStatementSyntax node)
			{
				var kind = ConvertToken(node.Kind(), TokenContext.Local);
				return SingleStatement(SyntaxFactory.AssignmentExpression(kind, (ExpressionSyntax)node.Left.Accept(nodesVisitor), (ExpressionSyntax)node.Right.Accept(nodesVisitor)));
			}

			public override SyntaxList<StatementSyntax> VisitThrowStatement(VBSyntax.ThrowStatementSyntax node)
			{
				return SingleStatement(SyntaxFactory.ThrowStatement((ExpressionSyntax)node.Expression?.Accept(nodesVisitor)));
			}

			public override SyntaxList<StatementSyntax> VisitReturnStatement(VBSyntax.ReturnStatementSyntax node)
			{
				return SingleStatement(SyntaxFactory.ReturnStatement((ExpressionSyntax)node.Expression?.Accept(nodesVisitor)));
			}

			public override SyntaxList<StatementSyntax> VisitYieldStatement(VBSyntax.YieldStatementSyntax node)
			{
				return SingleStatement(SyntaxFactory.YieldStatement(SyntaxKind.YieldReturnStatement, (ExpressionSyntax)node.Expression?.Accept(nodesVisitor)));
			}

			public override SyntaxList<StatementSyntax> VisitSingleLineIfStatement(VBSyntax.SingleLineIfStatementSyntax node)
			{
				var condition = (ExpressionSyntax)node.Condition.Accept(nodesVisitor);
				var block = SyntaxFactory.Block(node.Statements.SelectMany(s => s.Accept(this)));
				ElseClauseSyntax elseClause = null;

				if (node.ElseClause != null)
				{
					var elseBlock = SyntaxFactory.Block(node.ElseClause.Statements.SelectMany(s => s.Accept(this)));
					elseClause = SyntaxFactory.ElseClause(elseBlock);
				}
				return SingleStatement(SyntaxFactory.IfStatement(condition, block, elseClause));
			}

			public override SyntaxList<StatementSyntax> VisitMultiLineIfBlock(VBSyntax.MultiLineIfBlockSyntax node)
			{
				var condition = (ExpressionSyntax)node.IfStatement.Condition.Accept(nodesVisitor);
				var block = SyntaxFactory.Block(node.Statements.SelectMany(s => s.Accept(this)));
				ElseClauseSyntax elseClause = null;

				if (node.ElseBlock != null)
				{
					var elseBlock = SyntaxFactory.Block(node.ElseBlock.Statements.SelectMany(s => s.Accept(this)));
					elseClause = SyntaxFactory.ElseClause(elseBlock);
				}

				foreach (var elseIf in node.ElseIfBlocks.Reverse())
				{
					var elseBlock = SyntaxFactory.Block(elseIf.Statements.SelectMany(s => s.Accept(this)));
					var ifStmt = SyntaxFactory.IfStatement((ExpressionSyntax)elseIf.ElseIfStatement.Condition.Accept(nodesVisitor), elseBlock, elseClause);
					elseClause = SyntaxFactory.ElseClause(ifStmt);
				}

				return SingleStatement(SyntaxFactory.IfStatement(condition, block, elseClause));
			}

			public override SyntaxList<StatementSyntax> VisitForBlock(VBSyntax.ForBlockSyntax node)
			{
				var stmt = node.ForStatement;
				ExpressionSyntax startValue = (ExpressionSyntax)stmt.FromValue.Accept(nodesVisitor);
				VariableDeclarationSyntax declaration = null;
				ExpressionSyntax id;
				if (stmt.ControlVariable is VBSyntax.VariableDeclaratorSyntax) {
					var v = (VBSyntax.VariableDeclaratorSyntax)stmt.ControlVariable;
					declaration = SplitVariableDeclarations(v, nodesVisitor, semanticModel).Values.Single();
					declaration = declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(declaration.Variables[0].WithInitializer(SyntaxFactory.EqualsValueClause(startValue))));
					id = SyntaxFactory.IdentifierName(declaration.Variables[0].Identifier);
				} else {
					var v = (ExpressionSyntax)stmt.ControlVariable.Accept(nodesVisitor);
					startValue = SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, v, startValue);
					id = v;
				}

				var step = (ExpressionSyntax)stmt.StepClause?.StepValue.Accept(nodesVisitor);
				PrefixUnaryExpressionSyntax value = step.SkipParens() as PrefixUnaryExpressionSyntax;
				ExpressionSyntax condition;
				if (value == null) {
					condition = SyntaxFactory.BinaryExpression(SyntaxKind.LessThanOrEqualExpression, id, (ExpressionSyntax)stmt.ToValue.Accept(nodesVisitor));
				} else {
					condition = SyntaxFactory.BinaryExpression(SyntaxKind.GreaterThanOrEqualExpression, id, (ExpressionSyntax)stmt.ToValue.Accept(nodesVisitor));
				}
				if (step == null)
					step = SyntaxFactory.PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, id);
				else
					step = SyntaxFactory.AssignmentExpression(SyntaxKind.AddAssignmentExpression, id, step);
				var block = SyntaxFactory.Block(node.Statements.SelectMany(s => s.Accept(this)));
				return SingleStatement(SyntaxFactory.ForStatement(
					declaration,
					declaration != null ? SyntaxFactory.SeparatedList<ExpressionSyntax>() : SyntaxFactory.SingletonSeparatedList(startValue),
					condition,
					SyntaxFactory.SingletonSeparatedList(step),
					block));
			}

			public override SyntaxList<StatementSyntax> VisitForEachBlock(VBSyntax.ForEachBlockSyntax node)
			{
				var stmt = node.ForEachStatement;

				TypeSyntax type = null;
				SyntaxToken id;
				if (stmt.ControlVariable is VBSyntax.VariableDeclaratorSyntax)
				{
					var v = (VBSyntax.VariableDeclaratorSyntax)stmt.ControlVariable;
					var declaration = SplitVariableDeclarations(v, nodesVisitor, semanticModel).Values.Single();
					type = declaration.Type;
					id = declaration.Variables[0].Identifier;
				}
				else
				{
					var v = (IdentifierNameSyntax)stmt.ControlVariable.Accept(nodesVisitor);
					id = v.Identifier;
					type = SyntaxFactory.ParseTypeName("var");
				}

				var block = SyntaxFactory.Block(node.Statements.SelectMany(s => s.Accept(this)));
				return SingleStatement(SyntaxFactory.ForEachStatement(
						type,
						id,
						(ExpressionSyntax)stmt.Expression.Accept(nodesVisitor),
						block
					));
			}

			public override SyntaxList<StatementSyntax> VisitSelectBlock(VBSyntax.SelectBlockSyntax node)
			{
				var expr = (ExpressionSyntax)node.SelectStatement.Expression.Accept(nodesVisitor);
				SwitchStatementSyntax switchStatement;
				if (ConvertToSwitch(expr, node.CaseBlocks, out switchStatement))
					return SingleStatement(switchStatement);
				throw new NotSupportedException();
			}

			private bool ConvertToSwitch(ExpressionSyntax expr, SyntaxList<VBSyntax.CaseBlockSyntax> caseBlocks, out SwitchStatementSyntax switchStatement)
			{
				switchStatement = null;

				var sections = new List<SwitchSectionSyntax>();
				foreach (var block in caseBlocks)
				{
					var labels = SyntaxFactory.List<SwitchLabelSyntax>();
					foreach (var c in block.CaseStatement.Cases)
					{
						if (c is VBSyntax.SimpleCaseClauseSyntax) {
							var s = (VBSyntax.SimpleCaseClauseSyntax)c;
							labels.Add(SyntaxFactory.CaseSwitchLabel((ExpressionSyntax)s.Value.Accept(nodesVisitor)));
						} else if (c is VBSyntax.ElseCaseClauseSyntax) {
							labels.Add(SyntaxFactory.DefaultSwitchLabel());
						} else return false;
					}
					var list = SyntaxFactory.List(block.Statements.SelectMany(s => s.Accept(this)));
					sections.Add(SyntaxFactory.SwitchSection(labels, list));
				}
				switchStatement = SyntaxFactory.SwitchStatement(expr, SyntaxFactory.List(sections));
				return true;
			}

			public override SyntaxList<StatementSyntax> VisitTryBlock(VBSyntax.TryBlockSyntax node)
			{
				return base.VisitTryBlock(node);
			}

			public override SyntaxList<StatementSyntax> VisitSyncLockBlock(VBSyntax.SyncLockBlockSyntax node)
			{
				return SingleStatement(SyntaxFactory.LockStatement(
					(ExpressionSyntax)node.SyncLockStatement.Expression.Accept(nodesVisitor),
					SyntaxFactory.Block(node.Statements.SelectMany(s => s.Accept(this)))
				));
			}

			SyntaxList<StatementSyntax> SingleStatement(StatementSyntax statement)
			{
				return SyntaxFactory.SingletonList(statement);
			}

			SyntaxList<StatementSyntax> SingleStatement(ExpressionSyntax expression)
			{
				return SyntaxFactory.SingletonList<StatementSyntax>(SyntaxFactory.ExpressionStatement(expression));
			}
		}
	}
}
