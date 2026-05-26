using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSV
{
    public class CSVApi
    {

        public void SaveData(string data, string result, DateTime? time = null)
        {
            // D:\MyData 文件夹
            string path = @"D:\MyData";

            // 如果文件夹不存在，则创建
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            // 执行文件清理：删除 14 天前的自动保存文件
            CleanUpOldFiles(path);

            // 文件名为当天日期，例如 2025-12-29.csv
            string fileName = $"{path}\\{DateTime.Now:yyyy-MM-dd}.csv";

            // 如果文件不存在，先创建并写入表头
            if (!File.Exists(fileName))
            {
                using (FileStream fs = new FileStream(fileName, FileMode.Create, FileAccess.Write))
                using (StreamWriter sw = new StreamWriter(fs, Encoding.Default))
                {
                    sw.WriteLine("时间,Data,Result");
                }
            }

            // 追加写入数据行
            using (StreamWriter sw2 = new StreamWriter(fileName, true, Encoding.Default))
            {
                string timeStr = (time ?? DateTime.Now).ToString("HH-mm-ss");
                sw2.WriteLine($"{timeStr},{data},{result}");
            }
        }

        private void CleanUpOldFiles(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path, "*.csv");
                    DateTime threshold = DateTime.Now.AddDays(-14);
                    foreach (string file in files)
                    {
                        if (File.GetLastWriteTime(file) < threshold)
                        {
                            File.Delete(file);
                        }
                    }
                }
            }
            catch { }
        }

        public string ReadData()
        {
            string path = @"D:\MyData";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            //string fileName = path + "\\" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv";
            string fileName = $"{path}\\{DateTime.Now.ToString("yyyy-MM-dd")}.csv";

            StreamReader sr = new StreamReader(fileName, Encoding.Default);
            string str = sr.ReadToEnd();
            return str;
        }
    }
}
