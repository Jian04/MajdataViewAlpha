using System;
using System.Collections.Generic;

namespace MajdataCore
{
    public enum SlideCodeCommand
    {
        Start,
        A,
        B,
        C,
        P,
        Q,
        Key
    }

    [Serializable]
    public sealed class SlideCodeInstruction
    {
        public SlideCodeCommand command;
        public int parameter;
        public int offset;
        public int length;

        public bool IsNode => command is
            SlideCodeCommand.Start or SlideCodeCommand.A or
            SlideCodeCommand.B or SlideCodeCommand.C or SlideCodeCommand.Key;

        public bool IsOrbit => command is SlideCodeCommand.P or SlideCodeCommand.Q;
    }

    [Serializable]
    public sealed class SlideCodeData
    {
        public string source = string.Empty;
        public List<SlideCodeInstruction> instructions = new();
    }

    public static class SlideCodeParser
    {
        public static bool LooksLikeSlideCode(string expression)
        {
            if (string.IsNullOrEmpty(expression) ||
                expression[0] is < '1' or > '8')
                return false;

            for (var index = 1; index + 1 < expression.Length && expression[index] != '['; index++)
                if (expression[index] == 'K' &&
                    expression[index + 1] is >= '1' and <= '8')
                    return true;
            return false;
        }

        public static bool TryParse(
            string expression,
            out SlideCodeData code,
            out string error)
        {
            code = new SlideCodeData { source = expression ?? string.Empty };
            error = string.Empty;
            if (string.IsNullOrEmpty(expression) ||
                expression[0] is < '1' or > '8')
                return Fail(
                    "SlideCode 必须以 1-8 的起点开头",
                    "SLIDECODE MUST START WITH A KEY FROM 1 TO 8",
                    expression ?? string.Empty, out error);

            code.instructions.Add(new SlideCodeInstruction
            {
                command = SlideCodeCommand.Start,
                parameter = expression[0] - '0',
                offset = 0,
                length = 1
            });

            SlideCodeCommand? active = null;
            var activeOffset = -1;
            var activeParameterCount = 0;
            var keyCount = 0;
            for (var index = 1; index < expression.Length; index++)
            {
                var character = expression[index];
                if (char.IsLetter(character))
                {
                    if (active.HasValue && activeParameterCount == 0)
                        return MissingParameter(active.Value, expression, out error);
                    if (!TryReadCommand(character, out var command))
                        return Fail(
                            $"SlideCode 不支持指令「{character}」",
                            $"UNKNOWN SLIDECODE COMMAND '{character}'",
                            expression, out error);
                    if (command == SlideCodeCommand.C)
                    {
                        code.instructions.Add(new SlideCodeInstruction
                        {
                            command = command,
                            offset = index,
                            length = 1
                        });
                        active = null;
                        activeOffset = -1;
                        activeParameterCount = 0;
                        continue;
                    }

                    active = command;
                    activeOffset = index;
                    activeParameterCount = 0;
                    continue;
                }

                if (character is < '0' or > '9' || !active.HasValue)
                    return Fail(
                        $"SlideCode 在位置 {index + 1} 缺少指令或含有非法字符",
                        $"SLIDECODE HAS A MISSING COMMAND OR INVALID CHARACTER AT {index + 1}",
                        expression, out error);

                var parameter = character - '0';
                if (!ParameterAllowed(active.Value, parameter))
                    return Fail(
                        $"{CommandName(active.Value)} 的参数「{parameter}」不合法",
                        $"INVALID PARAMETER '{parameter}' FOR {CommandName(active.Value)}",
                        expression, out error);

                if (active.Value == SlideCodeCommand.Key)
                {
                    keyCount++;
                    if (keyCount != 1 || activeParameterCount != 0 ||
                        index != expression.Length - 1)
                        return Fail(
                            "K 必须只出现一次，并以 K1-K8 结束整条 SlideCode",
                            "K MUST APPEAR ONCE AS THE FINAL K1-K8 IN SLIDECODE",
                            expression, out error);
                }

                code.instructions.Add(new SlideCodeInstruction
                {
                    command = active.Value,
                    parameter = parameter,
                    offset = activeParameterCount == 0 ? activeOffset : index,
                    length = activeParameterCount == 0 ? 2 : 1
                });
                activeParameterCount++;
            }

            if (active.HasValue && activeParameterCount == 0)
                return MissingParameter(active.Value, expression, out error);
            if (keyCount != 1 ||
                code.instructions.Count < 2 ||
                code.instructions[^1].command != SlideCodeCommand.Key)
                return Fail(
                    "SlideCode 必须以 K1-K8 结束",
                    "SLIDECODE MUST END WITH K1-K8",
                    expression, out error);

            for (var index = 2; index < code.instructions.Count; index++)
            {
                var previous = code.instructions[index - 1];
                var current = code.instructions[index];
                if (previous.IsOrbit && current.IsOrbit &&
                    previous.command != current.command)
                    return Fail(
                        "P 与 Q 轨道不能直接相连，中间需要 A、B 或 C 节点",
                        "P AND Q ORBITS NEED AN A, B, OR C NODE BETWEEN THEM",
                        expression, out error);
                if (previous.IsOrbit && current.IsOrbit &&
                    (previous.parameter == 0 && current.parameter == 9 ||
                     previous.parameter == 9 && current.parameter == 0))
                    return Fail(
                        "0 号中央圈与 9 号最外圈不能直接切换",
                        "ORBIT 0 AND ORBIT 9 CANNOT CONNECT DIRECTLY",
                        expression, out error);
            }
            if (!ValidateReachability(code, out error))
                return false;
            return true;
        }

        private readonly struct Point
        {
            public Point(double x, double y)
            {
                X = x;
                Y = y;
            }

            public double X { get; }
            public double Y { get; }
        }

        private readonly struct Orbit
        {
            public Orbit(Point center, double radius)
            {
                Center = center;
                Radius = radius;
            }

            public Point Center { get; }
            public double Radius { get; }
        }

        private static bool ValidateReachability(
            SlideCodeData code, out string error)
        {
            for (var index = 1; index < code.instructions.Count; index++)
            {
                var previous = code.instructions[index - 1];
                var current = code.instructions[index];
                if (previous.IsNode && current.IsOrbit &&
                    IsInside(Node(previous), OrbitFor(current.parameter)) ||
                    previous.IsOrbit && current.IsNode &&
                    IsInside(Node(current), OrbitFor(previous.parameter)))
                {
                    return Fail(
                        "节点位于目标轨道内部，无法用切线进入或离开",
                        "A NODE INSIDE AN ORBIT CANNOT ENTER OR LEAVE IT TANGENTIALLY",
                        code.source, out error);
                }
            }
            error = string.Empty;
            return true;
        }

        private static bool IsInside(Point point, Orbit orbit)
        {
            var x = point.X - orbit.Center.X;
            var y = point.Y - orbit.Center.Y;
            return Math.Sqrt(x * x + y * y) < orbit.Radius - 0.000001d;
        }

        private static Point Node(SlideCodeInstruction instruction)
        {
            if (instruction.command == SlideCodeCommand.C)
                return new Point(0d, 0d);
            const double radius = 4.8d;
            var distance = instruction.command == SlideCodeCommand.B
                ? radius * Math.Cos(67.5d * Math.PI / 180d) /
                  Math.Cos(22.5d * Math.PI / 180d)
                : radius;
            return Polar(distance, KeyAngle(instruction.parameter));
        }

        private static Orbit OrbitFor(int index)
        {
            const double radius = 4.8d;
            if (index == 0)
                return new Orbit(
                    new Point(0d, 0d),
                    radius * Math.Cos(67.5d * Math.PI / 180d));
            if (index == 9)
                return new Orbit(new Point(0d, 0d), radius);
            var sideRadius = radius * Math.Cos(22.5d * Math.PI / 180d) * 0.5d;
            return new Orbit(
                Polar(sideRadius, OrbitAngle(index)), sideRadius);
        }

        private static double KeyAngle(int index) =>
            (112.5d - index * 45d) * Math.PI / 180d;

        private static double OrbitAngle(int index) =>
            (135d - index * 45d) * Math.PI / 180d;

        private static Point Polar(double radius, double angle) =>
            new Point(Math.Cos(angle) * radius, Math.Sin(angle) * radius);

        private static bool TryReadCommand(
            char value, out SlideCodeCommand command)
        {
            command = value switch
            {
                'A' => SlideCodeCommand.A,
                'B' => SlideCodeCommand.B,
                'C' => SlideCodeCommand.C,
                'P' => SlideCodeCommand.P,
                'Q' => SlideCodeCommand.Q,
                'K' => SlideCodeCommand.Key,
                _ => SlideCodeCommand.Start
            };
            return value is 'A' or 'B' or 'C' or 'P' or 'Q' or 'K';
        }

        private static bool ParameterAllowed(
            SlideCodeCommand command, int parameter) => command switch
        {
            SlideCodeCommand.P or SlideCodeCommand.Q => parameter is >= 0 and <= 9,
            SlideCodeCommand.A or SlideCodeCommand.B or SlideCodeCommand.Key =>
                parameter is >= 1 and <= 8,
            _ => false
        };

        private static bool MissingParameter(
            SlideCodeCommand command, string expression, out string error) =>
            Fail(
                $"{CommandName(command)} 后缺少参数",
                $"{CommandName(command)} IS MISSING ITS PARAMETER",
                expression, out error);

        private static string CommandName(SlideCodeCommand command) => command switch
        {
            SlideCodeCommand.Key => "K",
            _ => command.ToString()
        };

        private static bool Fail(
            string chinese,
            string english,
            string expression,
            out string error)
        {
            error = SlideSyntaxValidator.Diagnose(
                chinese, english, expression ?? string.Empty);
            return false;
        }
    }
}
