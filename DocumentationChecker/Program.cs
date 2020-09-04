using System;
using System.Collections.Generic;

namespace DocumentationChecker
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            System.Threading.Tasks.Task<List<string>> task = DocumentationChecker.CheckXMLCommentDocumentation("D:\\cis400\\Exercises\\DocumentationExercise\\DocumentationExerciseStarter\\DocumentationExercise.sln");

            task.Wait();
            List<string> issues = task.Result;
            foreach(var issue in issues)
            {
                Console.WriteLine(issue);
            }
        }
    }
}
