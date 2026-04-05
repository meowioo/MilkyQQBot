需要你手动改的现有文件：

1. Program.cs
- 在 DatabaseManager.Initialize(); 后面加一行：
  GameRepository.Initialize();
- 在 FunCommands.Register(commandHandler, milky, state); 后面加一行：
  GameCommands.Register(commandHandler, milky);

2. GroupConfigManager.cs
- 给 GroupFeatureConfig 增加：
  public bool GameEnabled { get; set; } = false;
- 给 GroupConfigManager 增加：
  public static bool IsGameEnabled(long groupId) => GetConfig(groupId).GameEnabled;
  public static bool ToggleGame(long groupId)
  {
      var config = GetConfig(groupId);
      config.GameEnabled = !config.GameEnabled;
      Save();
      return config.GameEnabled;
  }

3. MilkyQQBot.csproj
- 确保地图文件会复制到输出目录：
  <ItemGroup>
    <None Update="Assets\QQMap.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

4. 把地图图片放到：
   MilkyQQBot/Assets/QQMap.png
