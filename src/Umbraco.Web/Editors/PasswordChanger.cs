using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using System.Web.Http.ModelBinding;
using System.Web.Security;
using Umbraco.Core;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Core.Models.Identity;
using Umbraco.Core.Security;
using Umbraco.Core.Services;
using Umbraco.Web.Models;
using IUser = Umbraco.Core.Models.Membership.IUser;

namespace Umbraco.Web.Editors
{
    internal class PasswordChanger
    {
        private readonly ILogger _logger;
        private readonly IUserService _userService;

        public PasswordChanger(ILogger logger, IUserService userService)
        {
            _logger = logger;
            _userService = userService;
        }

        /// <summary>
        /// Changes the password for a user based on the many different rules and config options
        /// </summary>
        /// <param name="currentUser">The user performing the password save action</param>
        /// <param name="savingUser">The user who's password is being changed</param>
        /// <param name="passwordModel"></param>
        /// <param name="userMgr"></param>
        /// <returns></returns>
        public async Task<Attempt<PasswordChangedModel>> ChangePasswordWithIdentityAsync(
            IUser currentUser,
            IUser savingUser,
            ChangingPasswordModel passwordModel,
            BackOfficeUserManager<BackOfficeIdentityUser> userMgr)
        {
            if (passwordModel == null) throw new ArgumentNullException(nameof(passwordModel));
            if (userMgr == null) throw new ArgumentNullException(nameof(userMgr));

            //check if this identity implementation is powered by an underlying membership provider (it will be in most cases)
            var membershipPasswordHasher = userMgr.PasswordHasher as IMembershipProviderPasswordHasher;

            //check if this identity implementation is powered by an IUserAwarePasswordHasher (it will be by default in 7.7+ but not for upgrades)

            if (membershipPasswordHasher != null && !(userMgr.PasswordHasher is IUserAwarePasswordHasher<BackOfficeIdentityUser, int>))
            {
                //if this isn't using an IUserAwarePasswordHasher, then fallback to the old way
                if (membershipPasswordHasher.MembershipProvider.RequiresQuestionAndAnswer)
                    throw new NotSupportedException("Currently the user editor does not support providers that have RequiresQuestionAndAnswer specified");
                return ChangePasswordWithMembershipProvider(savingUser.Username, passwordModel, membershipPasswordHasher.MembershipProvider);
            }

            //if we are here, then a IUserAwarePasswordHasher is available, however we cannot proceed in that case if for some odd reason
            //the user has configured the membership provider to not be hashed. This will actually never occur because the BackOfficeUserManager
            //will throw if it's not hashed, but we should make sure to check anyways (i.e. in case we want to unit test!)
            if (membershipPasswordHasher != null && membershipPasswordHasher.MembershipProvider.PasswordFormat != MembershipPasswordFormat.Hashed)
            {
                throw new InvalidOperationException("The membership provider cannot have a password format of " + membershipPasswordHasher.MembershipProvider.PasswordFormat + " and be configured with secured hashed passwords");
            }

            //Are we resetting the password?? In ASP.NET Identity APIs, this flag indicates that an admin user is changing another user's password
            //without knowing the original password.
            if (passwordModel.Reset.HasValue && passwordModel.Reset.Value)
            {
                //if it's the current user, the current user cannot reset their own password
                if (currentUser.Username == savingUser.Username)
                {
                    return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Password reset is not allowed", new[] { "resetPassword" }) });
                }

                //if the current user has access to reset/manually change the password
                if (currentUser.HasSectionAccess(Umbraco.Core.Constants.Applications.Users) == false)
                {
                    return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("The current user is not authorized", new[] { "resetPassword" }) });
                }

                //ok, we should be able to reset it
                var resetToken = await userMgr.GeneratePasswordResetTokenAsync(savingUser.Id);
                var newPass = passwordModel.NewPassword.IsNullOrWhiteSpace()
                    ? userMgr.GeneratePassword()
                    : passwordModel.NewPassword;

                var resetResult = await userMgr.ResetPasswordAsync(savingUser.Id, resetToken, newPass);

                if (resetResult.Succeeded == false)
                {
                    var errors = string.Join(". ", resetResult.Errors);
                    _logger.Warn<PasswordChanger>($"Could not reset user password {errors}");
                    return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Could not reset password, errors: " + errors, new[] { "resetPassword" }) });
                }
                
                return Attempt.Succeed(new PasswordChangedModel());
            }

            //we're not resetting it so we need to try to change it.

            if (passwordModel.NewPassword.IsNullOrWhiteSpace())
            {
                return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Cannot set an empty password", new[] { "value" }) });
            }

            //we cannot arbitrarily change the password without knowing the old one and no old password was supplied - need to return an error
            //TODO: What if the current user is admin? We should allow manually changing then?
            if (passwordModel.OldPassword.IsNullOrWhiteSpace())
            {
                //if password retrieval is not enabled but there is no old password we cannot continue
                return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Password cannot be changed without the old password", new[] { "oldPassword" }) });
            }

            if (passwordModel.OldPassword.IsNullOrWhiteSpace() == false)
            {
                //if an old password is suplied try to change it
                var changeResult = await userMgr.ChangePasswordAsync(savingUser.Id, passwordModel.OldPassword, passwordModel.NewPassword);
                if (changeResult.Succeeded == false)
                {
                    var errors = string.Join(". ", changeResult.Errors);
                    _logger.Warn<PasswordChanger>($"Could not change user password {errors}");
                    return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Could not change password, errors: " + errors, new[] { "oldPassword" }) });
                }
                return Attempt.Succeed(new PasswordChangedModel());
            }

            //We shouldn't really get here
            return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Could not change password, invalid information supplied", new[] { "value" }) });
        }

        /// <summary>
        /// Changes password for a member/user given the membership provider and the password change model
        /// </summary>
        /// <param name="username">The username of the user having their password changed</param>
        /// <param name="passwordModel"></param>
        /// <param name="membershipProvider"></param>
        /// <returns></returns>
        public Attempt<PasswordChangedModel> ChangePasswordWithMembershipProvider(string username, ChangingPasswordModel passwordModel, MembershipProvider membershipProvider)
        {
            // YES! It is completely insane how many options you have to take into account based on the membership provider. yikes!

            if (passwordModel == null) throw new ArgumentNullException(nameof(passwordModel));
            if (membershipProvider == null) throw new ArgumentNullException(nameof(membershipProvider));

            //Are we resetting the password??
            if (passwordModel.Reset.HasValue && passwordModel.Reset.Value)
            {
                var canReset = membershipProvider.CanResetPassword(_userService);
                if (canReset == false)
                {
                    return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Password reset is not enabled", new[] { "resetPassword" }) });
                }
                if (membershipProvider.RequiresQuestionAndAnswer && passwordModel.Answer.IsNullOrWhiteSpace())
                {
                    return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Password reset requires a password answer", new[] { "resetPassword" }) });
                }
                //ok, we should be able to reset it
                try
                {
                    var newPass = membershipProvider.ResetPassword(
                        username,
                        membershipProvider.RequiresQuestionAndAnswer ? passwordModel.Answer : null);

                    //return the generated pword
                    return Attempt.Succeed(new PasswordChangedModel { ResetPassword = newPass });
                }
                catch (Exception ex)
                {
                    _logger.Warn<PasswordChanger>("Could not reset member password", ex);
                    return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Could not reset password, error: " + ex.Message + " (see log for full details)", new[] { "resetPassword" }) });
                }
            }

            //we're not resetting it so we need to try to change it.

            if (passwordModel.NewPassword.IsNullOrWhiteSpace())
            {
                return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Cannot set an empty password", new[] { "value" }) });
            }

            //This is an edge case and is only necessary for backwards compatibility:
            if (membershipProvider is MembershipProviderBase umbracoBaseProvider && umbracoBaseProvider.AllowManuallyChangingPassword)
            {
                //this provider allows manually changing the password without the old password, so we can just do it
                try
                {
                    var result = umbracoBaseProvider.ChangePassword(username, "", passwordModel.NewPassword);
                    return result == false
                        ? Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Could not change password, invalid username or password", new[] { "value" }) })
                        : Attempt.Succeed(new PasswordChangedModel());
                }
                catch (Exception ex)
                {
                    _logger.Warn<PasswordChanger>("Could not change member password", ex);
                    return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Could not change password, error: " + ex.Message + " (see log for full details)", new[] { "value" }) });
                }
            }

            //The provider does not support manually chaning the password but no old password supplied - need to return an error
            if (passwordModel.OldPassword.IsNullOrWhiteSpace() && membershipProvider.EnablePasswordRetrieval == false)
            {
                //if password retrieval is not enabled but there is no old password we cannot continue
                return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Password cannot be changed without the old password", new[] { "oldPassword" }) });
            }

            if (passwordModel.OldPassword.IsNullOrWhiteSpace() == false)
            {
                //if an old password is suplied try to change it

                try
                {
                    var result = membershipProvider.ChangePassword(username, passwordModel.OldPassword, passwordModel.NewPassword);
                    return result == false
                        ? Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Could not change password, invalid username or password", new[] { "oldPassword" }) })
                        : Attempt.Succeed(new PasswordChangedModel());
                }
                catch (Exception ex)
                {
                    _logger.Warn<PasswordChanger>("Could not change member password", ex);
                    return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Could not change password, error: " + ex.Message + " (see log for full details)", new[] { "value" }) });
                }
            }

            if (membershipProvider.EnablePasswordRetrieval == false)
            {
                //we cannot continue if we cannot get the current password
                return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Password cannot be changed without the old password", new[] { "oldPassword" }) });
            }
            if (membershipProvider.RequiresQuestionAndAnswer && passwordModel.Answer.IsNullOrWhiteSpace())
            {
                //if the question answer is required but there isn't one, we cannot continue
                return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Password cannot be changed without the password answer", new[] { "value" }) });
            }

            //lets try to get the old one so we can change it
            try
            {
                var oldPassword = membershipProvider.GetPassword(
                    username,
                    membershipProvider.RequiresQuestionAndAnswer ? passwordModel.Answer : null);

                try
                {
                    var result = membershipProvider.ChangePassword(username, oldPassword, passwordModel.NewPassword);
                    return result == false
                        ? Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Could not change password", new[] { "value" }) })
                        : Attempt.Succeed(new PasswordChangedModel());
                }
                catch (Exception ex1)
                {
                    _logger.Warn<PasswordChanger>("Could not change member password", ex1);
                    return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Could not change password, error: " + ex1.Message + " (see log for full details)", new[] { "value" }) });
                }

            }
            catch (Exception ex2)
            {
                _logger.Warn<PasswordChanger>("Could not retrieve member password", ex2);
                return Attempt.Fail(new PasswordChangedModel { ChangeError = new ValidationResult("Could not change password, error: " + ex2.Message + " (see log for full details)", new[] { "value" }) });
            }
        }

    }
}