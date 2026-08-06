using Xunit;

// 测试共享 DatabaseService/LevelService 单例与同一临时数据库，
// 必须串行执行，避免类间并行导致的 XP/状态断言互相干扰。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
