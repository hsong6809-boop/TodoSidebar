using System;
using TodoSidebar.Services;
using Xunit;

namespace TodoSidebar.Tests
{
    /// <summary>S4/T4：头像缓存按用户隔离，防止切号串图。</summary>
    public class AvatarPathIsolationTests
    {
        [Fact]
        public void GetAvatarPathForUser_DifferentUsers_DifferentFiles()
        {
            var a = AccountService.GetAvatarPathForUser("user-a-111");
            var b = AccountService.GetAvatarPathForUser("user-b-222");
            Assert.NotEqual(a, b);
            Assert.Contains("avatar-", System.IO.Path.GetFileName(a));
        }

        [Fact]
        public void GetAvatarPathForUser_EmptyOrUnsafe_FallsBack()
        {
            var empty = AccountService.GetAvatarPathForUser("");
            var unsafePath = AccountService.GetAvatarPathForUser("../..\\evil");
            Assert.False(string.IsNullOrEmpty(empty));
            Assert.DoesNotContain("..", empty);
            Assert.DoesNotContain("..", unsafePath);
            Assert.EndsWith(".png", empty);
        }

        [Fact]
        public void GetAvatarPathForUser_SameUser_Stable()
        {
            var a = AccountService.GetAvatarPathForUser("user-a-111");
            var b = AccountService.GetAvatarPathForUser("user-a-111");
            Assert.Equal(a, b);
        }
    }
}
