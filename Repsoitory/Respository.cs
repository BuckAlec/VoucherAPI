using FortressCodesDomain.DbModels;
using FortressDomain.Models.RequestObjects;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Migrations;
using System.Data.Entity.Validation;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Web;
using FortressDomain.Helpers;

namespace FortressCodesApi.Repsoitory
{
    public class Respository : IRepository
    {
        private FortressCodeContext db;

        private Exception ThrowDbEntityValidationException(DbEntityValidationException dbEx)
        {
            Exception raise = dbEx;
            foreach (var validationErrors in dbEx.EntityValidationErrors)
            {
                foreach (var validationError in validationErrors.ValidationErrors)
                {
                    string message = string.Format("{0}:{1}",
                        validationErrors.Entry.Entity.ToString(),
                        validationError.ErrorMessage);
                    // raise a new exception nesting the current instance as InnerException
                    raise = new InvalidOperationException(message, raise);
                }
            }
            return raise;
        }

        public Respository(FortressCodeContext db)
        {
            this.db = db;
        }

        public async Task<Voucher> GetCodeAsync(string code)
        {
            return await db.Vouchers.Include("TransactionType").FirstOrDefaultAsync(v => v.vouchercode == code);
        }

        public async Task<Voucher> GetCodeByIdAsync(int codeid)
        {
            return await db.Vouchers.Include("TransactionType").FirstOrDefaultAsync(v => v.Id == codeid);
        }

        public async Task<int> AddTransactionAsync(Transaction entity)
        {
            db.Transactions.Add(entity);
            return await db.SaveChangesAsync();
        }

        public async Task<List<TransactionType>> GetAllTransactionTypesAsync()
        {
            return await db.TransactionTypes.ToListAsync();
        }

        public async Task<TransactionType> GetTransactionTypeIdAsync(string transactionType)
        {
            return await db.TransactionTypes.SingleOrDefaultAsync(t => t.Name == transactionType);
        }

        public async Task<int> UpdateVoucherAsync(Voucher entity)
        {
            try
            {
                db.Vouchers.AddOrUpdate(entity);
                return await db.SaveChangesAsync();
            }
            catch (System.Data.Entity.Validation.DbEntityValidationException dbEx)
            {
                Exception raise = dbEx;
                foreach (var validationErrors in dbEx.EntityValidationErrors)
                {
                    foreach (var validationError in validationErrors.ValidationErrors)
                    {
                        string message = string.Format("{0}:{1}",
                            validationErrors.Entry.Entity.ToString(),
                            validationError.ErrorMessage);
                        // raise a new exception nesting the current instance as InnerException
                        raise = new InvalidOperationException(message, raise);
                    }
                }
                throw raise;
            }
            catch (Exception ex)
            {
                throw new Exception("Error updating voucher", ex);
            }
        }

        public async Task<int> GetCodeAttemptsInTimeLimitAsync(int codeId, int timeLimit, int validatedTransactionTypeId)
        {
            DateTime dateTime15MinsAgo = DateTime.Now.AddMinutes(-timeLimit);
            return await db.Transactions.CountAsync(t => t.CodeId == codeId && t.Date > dateTime15MinsAgo && t.TransactionTypeId == validatedTransactionTypeId);
        }

        public async Task<int> GetCodeUsageCountAsync(int codeId, int activatedTransactionTypeId)
        {
            return await db.Transactions.CountAsync(t => t.CodeId == codeId && t.TransactionTypeId == activatedTransactionTypeId);
        }
        public bool IsPostAuthorized(string secret, int UserID)
        {
            bool result = false;

            if (secret == null) return result;

            string token = Encryption.GetTodaysEncryptedToken(UserID);

            string decodedUploaded = HttpUtility.UrlDecode(secret);
            string decodedSource = HttpUtility.UrlDecode(token);

            return (decodedUploaded == decodedSource);
        }
        public async Task<PricingModel> GetPricingModelByVoucherAndDeviceLevel(Voucher voucher, string deviceLevel)
        {
            return await db.PricingModels.SingleOrDefaultAsync(
                    pm =>
                        pm.Level.Name == deviceLevel &&
                        pm.VoucherMetadatas.FirstOrDefault().Voucher.vouchercode == voucher.vouchercode &&
                        pm.Active == true);
        }

        /// <summary>
        /// TODO://  need to possibly look at returning a more meaningful error when the countries do not match,
        /// currently it will be a device level does not exist
        /// </summary>
        /// <param name="deviceMake">The device make.</param>
        /// <param name="deviceModel">The device model.</param>
        /// <param name="deviveCapactiy">The devive capactiy.</param>
        /// <param name="userDeviceCountryIso">The user device country iso.</param>
        /// <param name="pricingModel">The pricing model.</param>
        /// <returns></returns>
        public async Task<DeviceLevel> GetDeviceLevelByDeviceDetailsAsync(String deviceMake, String deviceModel,
                                                                          String deviveCapactiy, String userDeviceCountryIso,
                                                                            PricingModel pricingModel)
        {
            DeviceLevel ret = null;
            var device = await db.Devices.SingleOrDefaultAsync(d =>
                d.make.ToLower() == deviceMake.ToLower() &&
                d.model.ToLower() == deviceModel.ToLower() &&
                d.capacity.ToLower() == deviveCapactiy.ToLower() + "gb");

            if (device != null)
            {
                if (pricingModel.Country.ISO == userDeviceCountryIso)
                {
                    ret = device.DeviceLevels.SingleOrDefault(dl => dl.PartnerId == pricingModel.PartnerId);
                }
            }
            return ret;
        }


        public async Task<PricingModel> GetPricingModelByVoucherCodeAsync(String voucherCode)
        {
            PricingModel ret = null;
            var voucher = await db.Vouchers.SingleOrDefaultAsync(v => v.vouchercode == voucherCode);
            if (voucher != null)
            {
                var metadata = voucher.VoucherMetadatas.FirstOrDefault();
                if (metadata != null)
                {
                    if (metadata.PricingModel != null)
                    {
                        ret = metadata.PricingModel;
                    }
                }
            }
            return ret;
        }

        public async Task<PricingModel> GetPricingModelByDevicePartnerFamilyAsync(string deviceLevel, Int32 tierId, Int32 familyId)
        {
            PricingModel ret = null;
            ret = await db.PricingModels.SingleOrDefaultAsync(pm => pm.FamilyId == familyId && pm.Level.Name == deviceLevel && pm.TeirId == tierId && pm.Active == true);
            return ret;
        }

        public async Task<PricingModel> GetPricingModelByDeviceIdAsync(Int32 deviceId)
        {
            PricingModel ret = null;
            var device = await db.Devices.SingleOrDefaultAsync(d => d.id == deviceId);
            if (device != null)
            {
                var deviceLevel = device.DeviceLevels.SingleOrDefault();
                if (deviceLevel != null)
                {
                    var level = deviceLevel.Level;
                    if (level != null)
                    {
                        ret = level.PricingModels.SingleOrDefault();
                    }
                }
            }
            return ret;
        }
        public async Task<Tuple<Boolean, Int32, Int32>> GetDeviceLevelIDAsync(DeviceLevelRequest deviceLevelRequest)
        {
            Boolean bIsUnknownDevice = false;
            Int32 sLevelID = 0;
            Int32 sDeviceID = 0;
            String deviceCapacity = FortressDomain.Helpers.DeviceSizeHelper.CalculateDeviceTotalSizeFromRaw(deviceLevelRequest).ToString();

            var voucher = db.Vouchers.SingleOrDefault(v => v.vouchercode == deviceLevelRequest.VoucherCode);

            //Int32? iPartnerID = null;
            PricingModel pricingModel = null;
            if (voucher != null)
            {
                var metadata = voucher.VoucherMetadatas.FirstOrDefault();
                if (metadata != null)
                {
                    if (metadata.PricingModel != null)
                    {
                        pricingModel = metadata.PricingModel;
                        //iPartnerID = metadata.PricingModel.PartnerId;
                    }
                }
            }

            //match on the formatted name, and the partner id
            //TODO: include country lookup, but is it country of device or voucher

            //Check if the device the user has registered with is known to the system, if not return the unknown device
            var device = await GetDeviceByDeviceFieldsAsync(deviceLevelRequest.DeviceMake, deviceLevelRequest.DeviceModel, deviceCapacity);
            if (device == null)
            {
                var unknownDevice = await GetDeviceByFormattedDeviceNameAsync("Unknown Device");
                if (unknownDevice != null)
                {
                    bIsUnknownDevice = true;
                    //find the unknown device level that matches the voucher level
                    var unknownDeviceLevel = unknownDevice.DeviceLevels.SingleOrDefault(dl => dl.LevelId == pricingModel.LevelId);
                    if (unknownDeviceLevel != null)
                    {
                        sLevelID = unknownDeviceLevel.Id;
                    }
                }
            }
            else
            {
                sDeviceID = device.id;
                var deviceLevel = await GetDeviceLevelByDeviceDetailsAsync(deviceLevelRequest.DeviceMake,
                                                                           deviceLevelRequest.DeviceModel,
                                                                           deviceCapacity,
                                                                           deviceLevelRequest.UserCountryIso,
                                                                           pricingModel);
                if (deviceLevel != null)
                {
                    sLevelID = deviceLevel.Id;
                }
            }
            return new Tuple<Boolean, Int32, Int32>(bIsUnknownDevice, sLevelID, sDeviceID);
        }
        //TOOD - may need to replace with the codes domain repo method to avoid confusion/ code duplication
        public async Task<Tuple<Boolean, String>> GetDeviceLevelAsync(DeviceLevelRequest deviceLevelRequest)
        {
            Boolean bIsUnknownDevice = false;
            String sLevelName = null;

            String deviceCapacity = FortressDomain.Helpers.DeviceSizeHelper.CalculateDeviceTotalSizeFromRaw(deviceLevelRequest).ToString();

            var voucher = db.Vouchers.SingleOrDefault(v => v.vouchercode == deviceLevelRequest.VoucherCode);

            //Int32? iPartnerID = null;
            PricingModel pricingModel = null;
            if (voucher != null)
            {
                var metadata = voucher.VoucherMetadatas.FirstOrDefault();
                if (metadata != null)
                {
                    if (metadata.PricingModel != null)
                    {
                        pricingModel = metadata.PricingModel;
                        //iPartnerID = metadata.PricingModel.PartnerId;
                    }
                }
            }

            //match on the formatted name, and the partner id
            //TODO: include country lookup, but is it country of device or voucher

            //Check if the device the user has registered with is known to the system, if not return the unknown device
            var device = await GetDeviceByDeviceFieldsAsync(deviceLevelRequest.DeviceMake, deviceLevelRequest.DeviceModel, deviceCapacity);
            if (device == null)
            {
                var unknownDevice = await GetDeviceByFormattedDeviceNameAsync("Unknown Device");
                if (unknownDevice != null)
                {
                    bIsUnknownDevice = true;
                    //find the unknown device level that matches the voucher level
                    var unknownDeviceLevel = unknownDevice.DeviceLevels.SingleOrDefault(dl => dl.LevelId == pricingModel.LevelId);
                    if (unknownDeviceLevel != null)
                    {
                        sLevelName = unknownDeviceLevel.Level.Name;
                    }
                }
            }
            else
            {
                var deviceLevel = await GetDeviceLevelByDeviceDetailsAsync(deviceLevelRequest.DeviceMake,
                                                                           deviceLevelRequest.DeviceModel,
                                                                           deviceCapacity,
                                                                           deviceLevelRequest.UserCountryIso,
                                                                           pricingModel);
                if (deviceLevel != null)
                {
                    sLevelName = deviceLevel.Level.Name;
                }
            }
            return new Tuple<Boolean, String>(bIsUnknownDevice, sLevelName);
        }
        public async Task<Tuple<Boolean, String>> GetDeviceLevelAsyncMem(DeviceLevelRequestMem deviceLevelRequest)
        {
            Boolean bIsUnknownDevice = false;
            String sLevelName = null;
            DeviceLevelRequest dR = new DeviceLevelRequest();
            dR.DeviceCapacityRaw = deviceLevelRequest.DeviceCapacityRaw;
            dR.DeviceMake = deviceLevelRequest.DeviceMake;
            dR.DeviceModel = deviceLevelRequest.DeviceMake;
            dR.DeviceModelRaw = deviceLevelRequest.DeviceMake;
            dR.UserCountryIso = deviceLevelRequest.DeviceMake;
            String deviceCapacity = FortressDomain.Helpers.DeviceSizeHelper.CalculateDeviceTotalSizeFromRaw(dR).ToString();

            //Int32? iPartnerID = null;

            FortressCodesDomain.Repository.Respository repo = new FortressCodesDomain.Repository.Respository(new FortressCodesDomain.DbModels.FortressCodeContext());
            PricingModel pricingModel = await repo.GetPricingModelByIdAsync(deviceLevelRequest.PricingModelID);


            //match on the formatted name, and the partner id
            //TODO: include country lookup, but is it country of device or voucher

            //Check if the device the user has registered with is known to the system, if not return the unknown device
            var device = await GetDeviceByDeviceFieldsAsync(deviceLevelRequest.DeviceMake, deviceLevelRequest.DeviceModel, deviceCapacity);
            if (device == null)
            {
                var unknownDevice = await GetDeviceByFormattedDeviceNameAsync("Unknown Device");
                if (unknownDevice != null)
                {
                    bIsUnknownDevice = true;
                    //find the unknown device level that matches the voucher level
                    var unknownDeviceLevel = unknownDevice.DeviceLevels.SingleOrDefault(dl => dl.LevelId == pricingModel.LevelId);
                    if (unknownDeviceLevel != null)
                    {
                        sLevelName = unknownDeviceLevel.Level.Name;
                    }
                }
            }
            else
            {
                var deviceLevel = await GetDeviceLevelByDeviceDetailsAsync(deviceLevelRequest.DeviceMake,
                                                                           deviceLevelRequest.DeviceModel,
                                                                           deviceCapacity,
                                                                           deviceLevelRequest.UserCountryIso,
                                                                           pricingModel);
                if (deviceLevel != null)
                {
                    sLevelName = deviceLevel.Level.Name;
                }
            }
            return new Tuple<Boolean, String>(bIsUnknownDevice, sLevelName);
        }
        public async Task<DeviceLevel> GetDeviceLevelByIDAsync(Int32 DeviceLevelID)
        {
            return await db.DeviceLevels.SingleOrDefaultAsync(d => d.Id == DeviceLevelID);
        }
        public Boolean IsValid<T>(T entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException("The entity can not be null.");
            }
            return true;
        }


        public async Task<Boolean> AddAsync<T>(T entity) where T : class
        {
            if (!IsValid(entity))
            {
                return false;
            }
            try
            {
                db.Set(typeof(T)).Add(entity);
                await db.SaveChangesAsync();
                return db.Entry(entity).GetValidationResult().IsValid;
            }
            catch (System.Data.Entity.Validation.DbEntityValidationException dbEx)
            {
                throw ThrowDbEntityValidationException(dbEx);
            }
            catch (Exception ex)
            {
                throw new Exception(String.Format("Error updating {0}", entity.GetType().Name), ex);
            }
        }


        public async Task<Boolean> UpdateAsync<T>(T entity) where T : class
        {
            if (!IsValid(entity))
            {
                return false;
            }
            try
            {
                db.Set(typeof(T)).Attach(entity);
                db.Entry(entity).State = EntityState.Modified;
                await db.SaveChangesAsync();
                return db.Entry(entity).GetValidationResult().IsValid;
            }
            catch (System.Data.Entity.Validation.DbEntityValidationException dbEx)
            {
                throw ThrowDbEntityValidationException(dbEx);
            }
            catch (Exception ex)
            {
                throw new Exception(String.Format("Error updating {0}", entity.GetType().Name), ex);
            }
        }


        public async Task<Boolean> DeleteAsync<T>(T entity) where T : class
        {
            if (!IsValid(entity))
            {
                return false;
            }
            try
            {
                db.Set(typeof(T)).Remove(entity);
                await db.SaveChangesAsync();
                return db.Entry(entity).GetValidationResult().IsValid;
            }
            catch (System.Data.Entity.Validation.DbEntityValidationException dbEx)
            {
                throw ThrowDbEntityValidationException(dbEx);
            }
            catch (Exception ex)
            {
                throw new Exception(String.Format("Error updating {0}", entity.GetType().Name), ex);
            }
        }


        public IQueryable<T> GetAll<T>() where T : class
        {
            IQueryable<T> query = db.Set<T>();
            return query;
        }


        public IQueryable<T> FindBy<T>(Expression<Func<T, Boolean>> predicate) where T : class
        {

            IQueryable<T> query = db.Set<T>().Where(predicate);
            return query;
        }


        public T GetSingleOrDefault<T>(Expression<Func<T, Boolean>> predicate) where T : class
        {

            T query = db.Set<T>().SingleOrDefault(predicate);
            return query;
        }

        public async Task<Device> GetDeviceByFormattedDeviceNameAsync(String formattedDeviceName)
        {
            return await db.Devices.SingleOrDefaultAsync(d => d.name == formattedDeviceName.ToLower());
        }
        public async Task<Device> GetDeviceByIDAsync(Int32 DeviceID)
        {
            return await db.Devices.SingleOrDefaultAsync(d => d.id == DeviceID);
        }
        public async Task<Device> GetDeviceByDeviceFieldsAsync(String deviceMake, String deviceModel, String deviceCapacity)
        {
            return await db.Devices.SingleOrDefaultAsync(d =>
                d.make.ToLower() == deviceMake.ToLower() &&
                d.model.ToLower() == deviceModel.ToLower() &&
                d.capacity.ToLower() == deviceCapacity.ToLower() + "gb");
        }

        public async Task<IEnumerable<PricingModel>> GetPricingModelsByFamily(int familyId)
        {
            return await db.PricingModels.Where(pm => pm.FamilyId == familyId).ToListAsync();
        }

        public async Task<PricingModel> GetPricingModelsById(int id)
        {
            return await db.PricingModels.SingleOrDefaultAsync(pm => pm.Id == id);
        }
    }
}