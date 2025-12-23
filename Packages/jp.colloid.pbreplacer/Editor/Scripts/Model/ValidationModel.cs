using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace colloid.PBReplacer
{
    /// <summary>
    /// バリデーション問題のタイプ
    /// </summary>
    public enum ValidationProblemType
    {
        NullReference,
        MissingReference,
        InvalidValue
    }

    /// <summary>
    /// バリデーション問題の詳細情報
    /// </summary>
    public class ValidationProblem
    {
        public string ComponentName { get; set; }
	    public string ObjectName { get; set; }
	    public string PropertyPath { get; set; }
        public string PropertyName { get; set; }
        public ValidationProblemType ProblemType { get; set; }
        
        public override string ToString()
        {
	        string problem = ProblemType switch
	        {
	        	ValidationProblemType.NullReference => "未設定",
	        	ValidationProblemType.MissingReference => "Missing参照",
	        	_ => "無効な値"
	        };
            
	        return $"{ObjectName} > {ComponentName}: {char.ToUpper(PropertyPath[0])}{PropertyPath.Substring(1).Split(".")[0]}({PropertyName}) は {problem} です";
        }
    }

    /// <summary>
    /// バリデーション結果
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; } = true;
        public List<ValidationProblem> Problems { get; set; } = new List<ValidationProblem>();
        public Dictionary<Component, List<ValidationProblem>> ComponentProblems { get; set; } = 
            new Dictionary<Component, List<ValidationProblem>>();
        
        public string GetFormattedMessage()
        {
            if (IsValid)
                return "問題は見つかりませんでした。";
                
            var messageBuilder = new StringBuilder();
            messageBuilder.AppendLine("以下の問題が見つかりました：");
            
            foreach (var problem in Problems)
            {
                messageBuilder.AppendLine($"• {problem}");
            }
            
	        messageBuilder.AppendLine("\n処理を続行した場合、自動で問題部分を削除しますがこれにより予期しない動作が発生する可能性があります。");
	        messageBuilder.AppendLine();
	        messageBuilder.AppendLine("手動で修正する場合はキャンセルで処理を中断してください。");
            
            return messageBuilder.ToString();
        }
        
        public void AddProblem(Component component, ValidationProblem problem)
        {
            if (!ComponentProblems.ContainsKey(component))
                ComponentProblems[component] = new List<ValidationProblem>();
                
            ComponentProblems[component].Add(problem);
            Problems.Add(problem);
            IsValid = false;
        }
        
        public bool HasProblems(Component component)
        {
            return ComponentProblems.ContainsKey(component);
        }
        
        public List<ValidationProblem> GetProblems(Component component)
        {
            if (ComponentProblems.TryGetValue(component, out var problems))
                return problems;
            return new List<ValidationProblem>();
        }
    }
}