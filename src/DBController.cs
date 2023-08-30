using Microsoft.Data.Sqlite;

namespace TwitterAutomationTool
{
    class DBController
    {
        private const string ConnectionPath = @"Data Source=collections.db";

        /// <summary>
        /// SQLの実行
        /// </summary>
        /// <param name="connectString"></param>
        /// <param name="sqls"></param>
        public static void ExecuteNoneQuery(string sql)
        {
            using (SqliteConnection connection = new SqliteConnection(ConnectionPath))
            {
                connection.Open();

                using (SqliteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// SQLの実行
        /// </summary>
        /// <param name="connectString"></param>
        /// <param name="sqls"></param>
        public static void ExecuteNoneQueryWithTransaction(string[] sqls)
        {
            using (SqliteConnection connection = new SqliteConnection(ConnectionPath))
            {
                connection.Open();
                SqliteTransaction trans = connection.BeginTransaction();

                try
                {
                    foreach (string sql in sqls)
                    {
                        using (SqliteCommand cmd = connection.CreateCommand())
                        {
                            cmd.Transaction = trans;

                            cmd.CommandText = sql;
                            cmd.ExecuteNonQuery();
                            cmd.Dispose();
                        }
                    }
                    trans.Commit();
                }
                catch
                {
                    trans.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// スカラーによる単一データの取得
        /// </summary>
        /// <param name="connectString"></param>
        /// <param name="sql"></param>
        /// <returns></returns>
        public static object? ExecuteScalar(string sql)
        {
            object? result = null;

            using (SqliteConnection connection = new SqliteConnection(ConnectionPath))
            {
                connection.Open();

                using (SqliteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText = sql;
                    result = cmd.ExecuteScalar();
                }
            }

            return result;
        }

        /// <summary>
        /// DataReaderを使ったデータの取得
        /// </summary>
        /// <param name="connectString"></param>
        /// <param name="sql"></param>
        /// <returns></returns>
        public static List<object[]> ExecuteReader(string sql)
        {
            List<object[]> result = new List<object[]>();

            using (SqliteConnection connection = new SqliteConnection(ConnectionPath))
            {
                connection.Open();

                using (SqliteCommand cmd = connection.CreateCommand())
                {
                    //SQLの設定
                    cmd.CommandText = sql;

                    //検索
                    using (SqliteDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            object[] data = Enumerable.Range(0, reader.FieldCount).Select(i => reader[i]).ToArray();
                            result.Add(data);
                        }
                    }
                }
            }
            return result;
        }
    }
}